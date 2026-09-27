using Hba.Payment.Application.Common.Interfaces;
using Hba.Payment.Domain.Payments;
using Microsoft.EntityFrameworkCore;

namespace Hba.Payment.Infrastructure.Persistence.Repositories;

internal sealed class PaymentIntentRepository(PaymentDbContext context) : IPaymentIntentRepository
{
    public Task<PaymentIntent?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
        => context.PaymentIntents.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    public Task<PaymentIntent?> FindByProviderReferenceAsync(
        string providerReference,
        CancellationToken cancellationToken)
        => context.PaymentIntents
            .FirstOrDefaultAsync(p => p.ProviderReference == providerReference, cancellationToken);

    /// <summary>
    /// « Vivante » veut dire : encore payable, ou deja payee. Une intention en
    /// echec n'en est pas une — le client doit pouvoir reessayer.
    /// </summary>
    public Task<PaymentIntent?> FindActiveByDeliveryAsync(Guid deliveryId, CancellationToken cancellationToken)
        => context.PaymentIntents
            .Where(p => p.DeliveryId == deliveryId
                        && (p.Status == PaymentStatus.Pending || p.Status == PaymentStatus.Succeeded))
            .OrderByDescending(p => p.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

    public void Add(PaymentIntent intent) => context.PaymentIntents.Add(intent);
}
