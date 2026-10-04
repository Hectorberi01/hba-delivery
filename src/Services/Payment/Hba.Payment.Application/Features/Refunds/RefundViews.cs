using Hba.Payment.Domain.Payments;
using Hba.Payment.Domain.Refunds;

namespace Hba.Payment.Application.Features.Refunds;

/// <summary>Une ligne de la file, telle que la console la montre.</summary>
public sealed record RefundDueView(
    Guid Id,
    ServiceType ServiceType,
    Guid ReferenceId,
    Guid PaymentIntentId,
    string PayerId,
    long AmountCollected,

    /// <summary>
    /// NUL VEUT DIRE « A DETERMINER », pas « rien ». La console doit afficher les
    /// deux differemment : un zero se lirait « aucun remboursement du », ce qui est
    /// une decision que personne n'a prise.
    /// </summary>
    long? RecommendedRefund,

    string Reason,
    RefundDueStatus Status,
    long? SettledAmount,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ResolvedAt,
    string? ResolvedBy,
    string? Note)
{
    public static RefundDueView From(RefundDue refund)
    {
        ArgumentNullException.ThrowIfNull(refund);

        return new RefundDueView(
            refund.Id,
            refund.ServiceType,
            refund.ReferenceId,
            refund.PaymentIntentId,
            refund.PayerId,
            refund.AmountCollected,
            refund.RecommendedRefund,
            refund.Reason,
            refund.Status,
            refund.SettledAmount,
            refund.CreatedAt,
            refund.ResolvedAt,
            refund.ResolvedBy,
            refund.Note);
    }
}

public sealed record RefundDuePageView(IReadOnlyList<RefundDueView> Items, int Total);
