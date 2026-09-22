using Hba.BuildingBlocks.Application.Abstractions;
using Hba.BuildingBlocks.Application.Messaging;
using Hba.Delivery.Application.Authorization;
using Hba.Delivery.Application.Ports;
using Hba.Delivery.Application.Views;
using Hba.Delivery.Domain.Deliveries;

namespace Hba.Delivery.Application.Queries;

public sealed record ListDeliveriesQuery(
    IReadOnlyCollection<DeliveryStatus>? Statuses,
    DateTimeOffset? CreatedAfter,
    DateTimeOffset? CreatedBefore,
    int PageSize,
    int Offset) : IQuery<IReadOnlyList<DeliveryView>>;

public sealed class ListDeliveriesHandler(
    IDeliveryRepository repository,
    ICallerContext caller) : IQueryHandler<ListDeliveriesQuery, IReadOnlyList<DeliveryView>>
{
    private const int MaxPageSize = 100;

    public async Task<IReadOnlyList<DeliveryView>> HandleAsync(
        ListDeliveriesQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var filter = new DeliveryQueryFilter
        {
            Statuses = query.Statuses,
            CreatedAfter = query.CreatedAfter,
            CreatedBefore = query.CreatedBefore,
            PageSize = Math.Clamp(query.PageSize <= 0 ? 25 : query.PageSize, 1, MaxPageSize),
            Offset = Math.Max(0, query.Offset),
        };

        // Le périmètre est imposé ici, pas demandé à l'appelant.
        var scoped = DeliveryAccess.ScopeFor(caller, filter);

        var deliveries = await repository.ListAsync(scoped, cancellationToken).ConfigureAwait(false);

        return [.. deliveries.Select(d => DeliveryViewMapper.ToView(d, caller))];
    }
}
