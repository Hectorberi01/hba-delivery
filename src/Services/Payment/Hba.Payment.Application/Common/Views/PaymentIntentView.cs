using Hba.Payment.Domain.Payments;

namespace Hba.Payment.Application.Common.Views;

/// <summary>
/// Intention telle qu'elle sort du service. Le montant est un entier de francs
/// CFA (ADR 0006).
/// </summary>
public sealed record PaymentIntentView(
    Guid Id,
    Guid DeliveryId,
    string PayerId,
    long Amount,
    PaymentStatus Status,
    PaymentMethod Method,
    string ProviderReference,
    string RedirectUrl,
    DateTimeOffset CreatedAt,
    DateTimeOffset? SucceededAt,
    DateTimeOffset ExpiresAt)
{
    public static PaymentIntentView From(PaymentIntent intent)
    {
        ArgumentNullException.ThrowIfNull(intent);

        return new PaymentIntentView(
            intent.Id,
            intent.DeliveryId,
            intent.PayerId,
            intent.Amount.Amount,
            intent.Status,
            intent.Method,
            intent.ProviderReference ?? string.Empty,
            intent.RedirectUrl ?? string.Empty,
            intent.CreatedAt,
            intent.SucceededAt,
            intent.ExpiresAt);
    }
}
