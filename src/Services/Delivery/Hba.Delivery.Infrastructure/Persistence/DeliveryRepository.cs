using Hba.Delivery.Application.Ports;
using Microsoft.EntityFrameworkCore;

namespace Hba.Delivery.Infrastructure.Persistence;

internal sealed class DeliveryRepository(DeliveryDbContext context) : IDeliveryRepository
{
    public Task<DeliveryAggregate?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
        => context.Deliveries.FirstOrDefaultAsync(d => d.Id == id, cancellationToken);

    public Task<DeliveryAggregate?> GetByExternalOrderIdAsync(
        string partnerId,
        string externalOrderId,
        CancellationToken cancellationToken)
        => context.Deliveries.FirstOrDefaultAsync(
            d => d.PartnerId == partnerId && d.ExternalOrderId == externalOrderId,
            cancellationToken);

    public async Task<IReadOnlyList<DeliveryAggregate>> ListAsync(
        DeliveryQueryFilter filter,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var query = context.Deliveries.AsNoTracking().AsQueryable();

        if (filter.CustomerId is not null)
        {
            query = query.Where(d => d.CustomerId == filter.CustomerId);
        }

        if (filter.MerchantId is not null)
        {
            query = query.Where(d => d.MerchantId == filter.MerchantId);
        }

        if (filter.PartnerId is not null)
        {
            query = query.Where(d => d.PartnerId == filter.PartnerId);
        }

        if (filter.DriverId is not null)
        {
            query = query.Where(d => d.Driver != null && d.Driver.DriverId == filter.DriverId);
        }

        if (filter.Statuses is { Count: > 0 })
        {
            var statuses = filter.Statuses.ToList();
            query = query.Where(d => statuses.Contains(d.Status));
        }

        if (filter.CreatedAfter is not null)
        {
            query = query.Where(d => d.CreatedAt >= filter.CreatedAfter);
        }

        if (filter.CreatedBefore is not null)
        {
            query = query.Where(d => d.CreatedAt <= filter.CreatedBefore);
        }

        return await query
            .OrderByDescending(d => d.CreatedAt)
            .Skip(filter.Offset)
            .Take(filter.PageSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public void Add(DeliveryAggregate delivery) => context.Deliveries.Add(delivery);
}
