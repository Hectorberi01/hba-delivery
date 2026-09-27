using Hba.BuildingBlocks.Domain;
using Hba.Dispatch.Domain.Exceptions;

namespace Hba.Dispatch.Domain.Dispatching;

/// <summary>
/// Proposition faite a un livreur pendant une vague.
///
/// ELLE NE PORTE PAS L'ADRESSE DE DESTINATION. Le referentiel est explicite :
/// le livreur « ne voit l'adresse exacte de destination qu'apres acceptation ».
/// L'apercu se limite au repere de collecte, aux distances et a la
/// remuneration — de quoi decider, pas de quoi contourner la course.
/// </summary>
public sealed class Offer : Entity
{
    private Offer()
    {
    }

    /// <summary>
    /// L'IDENTIFIANT EST POSE ICI, ET IL DOIT L'ETRE.
    ///
    /// LA RAISON EST OfferSent. L'evenement porte l'identifiant de l'offre, et
    /// il est construit dans la meme methode que l'offre, donc AVANT
    /// SaveChanges. Si la cle venait d'un generateur EF, elle serait encore
    /// vide a cet instant : le message parti sur Kafka annoncerait une offre
    /// d'identifiant 00000000-0000-0000-0000-000000000000. Personne ne le lit
    /// aujourd'hui — Driver ne se sert que du driver_id pour reserver le
    /// livreur — et c'est bien pour cela que le defaut passerait des mois sans
    /// se voir.
    ///
    /// ALORS POURQUOI L'UPDATE FANTOME N'ARRIVE PAS. Le piege existe : quand
    /// EF decouvre un enfant dans une collection possedee dont le
    /// proprietaire est deja en base, il tranche « neuf ou existant ? » en
    /// regardant si la cle est renseignee — ET si elle est declaree
    /// store-generated. Une cle posee par le domaine sur une propriete
    /// ValueGeneratedOnAdd lui fait conclure que la ligne existe : UPDATE sur
    /// une ligne jamais ecrite, zero ligne affectee,
    /// DbUpdateConcurrencyException. C'est exactement ce que le journal SQL a
    /// montre sur les pieces du dossier livreur le 27 septembre 2026, ou la
    /// convention EF rendait la cle Guid ValueGeneratedOnAdd sans que personne
    /// l'ait demande.
    ///
    /// LE LEVIER EST DONC ValueGeneratedNever, PAS LE GENERATEUR. Il est pose
    /// dans DispatchConfiguration. Cle connue du domaine, ligne inseree, et
    /// l'evenement porte un identifiant vrai : les trois a la fois.
    /// </summary>
    internal Offer(
        string driverId,
        int waveNumber,
        int distanceToPickupMeters,
        DateTimeOffset sentAt,
        DateTimeOffset expiresAt)
        : base(Guid.CreateVersion7())
    {
        DriverId = driverId;
        WaveNumber = waveNumber;
        DistanceToPickupMeters = distanceToPickupMeters;
        SentAt = sentAt;
        ExpiresAt = expiresAt;
        Status = OfferStatus.Pending;
    }

    public string DriverId { get; private set; } = string.Empty;

    public OfferStatus Status { get; private set; }

    public int WaveNumber { get; private set; }

    public int DistanceToPickupMeters { get; private set; }

    public DateTimeOffset SentAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset? ResolvedAt { get; private set; }

    public string? DeclineReason { get; private set; }

    public bool IsPending => Status == OfferStatus.Pending;

    public bool HasExpiredAt(DateTimeOffset instant) => instant >= ExpiresAt;

    internal void Accept(DateTimeOffset now)
    {
        if (Status != OfferStatus.Pending)
        {
            throw new DomainException(
                Status == OfferStatus.Accepted ? DispatchErrorCodes.AlreadyTaken : DispatchErrorCodes.OfferExpired,
                "Cette offre n'est plus en attente.");
        }

        // LE DELAI SE VERIFIE ICI, PAS SEULEMENT AU BALAYAGE. Le planificateur
        // passe periodiquement ; entre deux passages une offre peut etre
        // echue sans avoir encore ete marquee. Accepter sur la foi du statut
        // seul donnerait la course a un livreur hors delai, au detriment de
        // celui de la vague suivante.
        if (HasExpiredAt(now))
        {
            Expire(now);
            throw new DomainException(DispatchErrorCodes.OfferExpired, "Cette offre a expire.");
        }

        Status = OfferStatus.Accepted;
        ResolvedAt = now;
    }

    internal void Decline(string? reason, DateTimeOffset now)
    {
        if (Status != OfferStatus.Pending)
        {
            return;
        }

        Status = OfferStatus.Declined;
        DeclineReason = reason;
        ResolvedAt = now;
    }

    internal void Expire(DateTimeOffset now)
    {
        if (Status != OfferStatus.Pending)
        {
            return;
        }

        Status = OfferStatus.Expired;
        ResolvedAt = now;
    }

    /// <summary>Une autre offre de la meme vague a gagne.</summary>
    internal void Supersede(DateTimeOffset now)
    {
        if (Status != OfferStatus.Pending)
        {
            return;
        }

        Status = OfferStatus.Superseded;
        ResolvedAt = now;
    }
}
