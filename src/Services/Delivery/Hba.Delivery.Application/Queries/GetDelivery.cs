using Hba.BuildingBlocks.Application.Abstractions;
using Hba.BuildingBlocks.Application.Messaging;
using Hba.BuildingBlocks.Domain;
using Hba.Delivery.Application.Authorization;
using Hba.Delivery.Application.Ports;
using Hba.Delivery.Application.Views;

namespace Hba.Delivery.Application.Queries;

public sealed record GetDeliveryQuery(Guid DeliveryId) : IQuery<DeliveryView>;

public sealed class GetDeliveryHandler(
    IDeliveryRepository repository,
    ICallerContext caller) : IQueryHandler<GetDeliveryQuery, DeliveryView>
{
    public async Task<DeliveryView> HandleAsync(GetDeliveryQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var delivery = await repository.GetByIdAsync(query.DeliveryId, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException("Livraison", query.DeliveryId.ToString());

        DeliveryAccess.EnsureCanRead(delivery, caller);

        return DeliveryViewMapper.ToView(delivery, caller);
    }
}
