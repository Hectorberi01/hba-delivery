using Hba.BuildingBlocks.Domain;
using Hba.Rating.Domain.Exceptions;

namespace Hba.Rating.Domain.Reviews;

/// <summary>
/// Ce qui est note : une mission terminee, et le livreur qui l'a portee.
/// </summary>
///
/// <remarks>
/// IL REPREND hba.common.v1.ServiceType, NUMEROS COMPRIS. Rating note des
/// courses de personnes et des livraisons de colis, et devra un jour noter autre
/// chose ; recopier l'enumere plutot que de le partager laisserait deux listes
/// diverger en silence.
/// </remarks>
public enum MissionType
{
    Unspecified = 0,
    Delivery = 1,
    Ride = 2,
}

/// <summary>
/// Un avis : une mission, un client, un livreur, et une note qui vient plus
/// tard — ou jamais.
/// </summary>
///
/// <remarks>
/// IL NAIT VIDE, A LA FIN DE LA MISSION, ET C'EST TOUT LE DECOUPAGE. Une ligne
/// est ouverte des que la mission s'acheve, sans note : elle dit « ce client
/// peut noter ce livreur pour cette mission-la ». La note arrive ensuite, si
/// elle arrive.
///
/// POURQUOI PAS UNE LIGNE CREEE AU MOMENT DE NOTER. Parce que l'application
/// devrait alors dire QUI elle note, et un client pourrait noter un livreur qui
/// ne l'a jamais servi — ou le meme deux fois. Le couple (mission, livreur) vient
/// de l'evenement du service concerne, jamais du telephone.
///
/// LE PRIX DE CE CHOIX EST UNE LIGNE PAR MISSION, NOTEE OU NON. C'est assume :
/// c'est aussi ce qui permet de repondre a « combien de clients notent », qui est
/// la premiere question qu'on se posera.
///
/// AUCUNE REGLE DE RETENTION N'EST ECRITE ICI. La fenetre pendant laquelle on
/// peut encore noter reste ouverte au point 41 ; tant qu'elle n'est pas tranchee,
/// une notation ouverte le reste.
/// </remarks>
public sealed class Review : AggregateRoot
{
    /// <summary>Le bareme, et il commence a une etoile.</summary>
    ///
    /// <remarks>
    /// ZERO N'EST PAS UNE NOTE, C'EST L'ABSENCE DE NOTE. Les deux se confondraient
    /// dans un entier non nullable, et une moyenne compterait alors les missions
    /// non notees comme des zeros — ce qui ecraserait la reputation de tout le
    /// monde.
    /// </remarks>
    public const int NoteMinimale = 1;

    public const int NoteMaximale = 5;

    /// <summary>Longueur retenue pour un commentaire libre.</summary>
    ///
    /// <remarks>
    /// DEUX CENTS CARACTERES, ET C'EST UNE BORNE DE PRUDENCE AUTANT QUE DE PLACE.
    /// Aucune moderation n'existe au MVP : plus le champ est long, plus ce qu'il
    /// peut contenir demande d'etre lu par quelqu'un.
    /// </remarks>
    public const int LongueurMaximaleDuCommentaire = 200;

    private Review()
    {
    }

    private Review(
        Guid id,
        MissionType missionType,
        Guid referenceId,
        string customerId,
        Guid driverId,
        DateTimeOffset missionCompletedAt)
        : base(id)
    {
        MissionType = missionType;
        ReferenceId = referenceId;
        CustomerId = customerId;
        DriverId = driverId;
        MissionCompletedAt = missionCompletedAt;
    }

    public MissionType MissionType { get; private set; }

    /// <summary>La course ou la livraison. Le type dit a qui appartient ce numero.</summary>
    public Guid ReferenceId { get; private set; }

    public string CustomerId { get; private set; } = string.Empty;

    public Guid DriverId { get; private set; }

    public DateTimeOffset MissionCompletedAt { get; private set; }

    /// <summary>Nul tant que le client n'a rien dit.</summary>
    public int? Stars { get; private set; }

    public string? Comment { get; private set; }

    public DateTimeOffset? SubmittedAt { get; private set; }

    public bool EstNotee => Stars is not null;

    /// <summary>Ouvre la notation d'une mission qui vient de s'achever.</summary>
    public static Review Ouvrir(
        Guid id,
        MissionType missionType,
        Guid referenceId,
        string customerId,
        Guid driverId,
        DateTimeOffset missionCompletedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(customerId);

        return new Review(id, missionType, referenceId, customerId, driverId, missionCompletedAt);
    }

    /// <summary>
    /// Le client donne sa note.
    /// </summary>
    ///
    /// <remarks>
    /// UNE SEULE FOIS, ET SANS RETOUR. Rouvrir une note ouvre la porte a la
    /// pression : un livreur qui demande au client de la corriger, un client qui
    /// la baisse apres coup pour obtenir un geste. Tant que personne n'a tranche
    /// cette question, le plus sur est de ne pas pouvoir.
    ///
    /// LE COMMENTAIRE EST FACULTATIF, et vide vaut absent : une chaine de
    /// quelques espaces n'apprend rien a personne et encombrerait l'ecran du
    /// livreur.
    /// </remarks>
    public void Soumettre(int stars, string? comment, DateTimeOffset now)
    {
        if (EstNotee)
        {
            throw new DomainException(
                RatingErrorCodes.AlreadyReviewed,
                "Cette mission a deja ete notee.");
        }

        if (stars is < NoteMinimale or > NoteMaximale)
        {
            throw new DomainException(
                RatingErrorCodes.InvalidStars,
                $"La note va de {NoteMinimale} a {NoteMaximale} etoiles.");
        }

        var propre = comment?.Trim();

        if (propre is { Length: > LongueurMaximaleDuCommentaire })
        {
            throw new DomainException(
                RatingErrorCodes.CommentTooLong,
                $"Le commentaire ne depasse pas {LongueurMaximaleDuCommentaire} caracteres.");
        }

        Stars = stars;
        Comment = string.IsNullOrWhiteSpace(propre) ? null : propre;
        SubmittedAt = now;
    }
}
