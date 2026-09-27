using Hba.Payment.Domain.Payouts;

namespace Hba.Payment.Application.Common.Interfaces;

/// <summary>Une demande de versement, telle qu'elle s'affiche.</summary>
public sealed record PayoutRequestView(
    Guid Id,
    string DriverId,
    long AmountXof,
    PayoutStatus Status,
    DateTimeOffset RequestedAt,
    DateTimeOffset? DecidedAt,
    // L'AUTEUR DE LA DECISION, tel que le porte son jeton. LE BACK-OFFICE LE
    // VOIT, LE LIVREUR NON, et c'est la passerelle qui tranche parce que c'est
    // la qu'on sait a qui l'on parle : un livreur n'a rien a faire de
    // l'identifiant d'un compte interne, la finance a besoin de savoir qui a
    // engage la maison.
    string? DecidedBy,
    string? RejectionReason,
    DateTimeOffset? PaidAt,
    string? PaymentReference);

public interface IPayoutRequestRepository
{
    Task<PayoutRequest?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// La demande en cours de ce livreur — demandee ou approuvee — s'il y en
    /// a une. UNE SEULE A LA FOIS : deux demandes simultanees videraient deux
    /// fois le meme solde.
    /// </summary>
    Task<PayoutRequest?> FindPendingAsync(string driverId, CancellationToken cancellationToken);

    /// <summary>Les demandes d'un livreur, la plus recente d'abord.</summary>
    Task<IReadOnlyList<PayoutRequestView>> ListForDriverAsync(
        string driverId,
        int limit,
        CancellationToken cancellationToken);

    /// <summary>
    /// La file de la finance : les demandes de TOUS les livreurs.
    ///
    /// DEUX ORDRES, PARCE QU'IL Y A DEUX USAGES. Sans etat demande, c'est la
    /// file d'attente — les demandes vivantes, LA PLUS ANCIENNE D'ABORD,
    /// parce qu'une file se sert dans l'ordre d'arrivee et qu'un livreur qui
    /// attend depuis trois jours ne doit pas glisser en bas de l'ecran chaque
    /// fois qu'un autre demande. Avec un etat, c'est un historique, et un
    /// historique se lit du plus recent.
    ///
    /// AUCUN FILTRE PAR LIVREUR ICI : la finance instruit une file, pas un
    /// dossier. Pour un livreur precis, <see cref="ListForDriverAsync"/>.
    /// </summary>
    Task<IReadOnlyList<PayoutRequestView>> ListForReviewAsync(
        PayoutStatus? status,
        int limit,
        CancellationToken cancellationToken);

    void Add(PayoutRequest request);
}
