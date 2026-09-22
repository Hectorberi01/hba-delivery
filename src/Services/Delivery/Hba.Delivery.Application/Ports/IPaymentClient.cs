using Hba.Delivery.Domain.ValueObjects;

namespace Hba.Delivery.Application.Ports;

/// <summary>
/// Accès au service Payment. Le fournisseur (FedaPay) reste derrière : Delivery
/// ne le connaît pas.
/// </summary>
public interface IPaymentClient
{
    Task<PaymentIntentResult> CreateIntentAsync(
        string idempotencyKey,
        Guid deliveryId,
        string payerId,
        string payerPhone,
        MoneyXof amount,
        CancellationToken cancellationToken);
}

public sealed record PaymentIntentResult(string PaymentIntentId, string RedirectUrl);
