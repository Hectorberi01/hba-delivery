using Hba.Payment.Domain.Payments;
using Hba.Payment.Domain.Refunds;

namespace Hba.Payment.Application.Common.Interfaces;

public interface IRefundDueRepository
{
    Task<RefundDue?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// La ligne deja ouverte pour cette intention, s'il y en a une.
    /// </summary>
    ///
    /// <remarks>
    /// DEUXIEME GARDE-FOU, APRES L'INBOX. L'Inbox ecarte le meme evenement livre
    /// deux fois ; celle-ci ecarte le meme FAIT arrive par un autre chemin — une
    /// course annulee puis declaree sans chauffeur, un rattrapage depuis l'origine
    /// du sujet. Deux lignes pour un seul encaissement feraient rembourser deux
    /// fois, et c'est de l'argent qui part.
    /// </remarks>
    Task<RefundDue?> FindByPaymentIntentAsync(Guid paymentIntentId, CancellationToken cancellationToken);

    /// <summary>
    /// La file, du plus ancien au plus recent.
    ///
    /// LE PLUS ANCIEN D'ABORD, ET C'EST L'INVERSE DES AUTRES LISTES DU DEPOT. Un
    /// remboursement qui attend depuis trois jours est plus urgent que celui d'il y a
    /// une heure : trier par date decroissante enterrerait les oublis sous les
    /// nouveaux.
    /// </remarks>
    Task<(IReadOnlyList<RefundDue> Lignes, int Total)> ListAsync(
        RefundDueStatus? status,
        ServiceType? serviceType,
        int pageSize,
        int offset,
        CancellationToken cancellationToken);

    void Add(RefundDue refund);
}
