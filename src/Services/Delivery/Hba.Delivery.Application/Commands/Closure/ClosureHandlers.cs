using Hba.BuildingBlocks.Application.Abstractions;
using Hba.BuildingBlocks.Application.Messaging;
using Hba.BuildingBlocks.Domain;
using Hba.BuildingBlocks.Security;
using Hba.Delivery.Application.Authorization;
using Hba.Delivery.Application.Ports;
using Hba.Delivery.Application.Views;

namespace Hba.Delivery.Application.Commands.Closure;

public sealed class CancelDeliveryHandler(
    IDeliveryRepository repository,
    IUnitOfWork unitOfWork,
    ICallerContext caller,
    IClock clock) : ICommandHandler<CancelDeliveryCommand, DeliveryView>
{
    public async Task<DeliveryView> HandleAsync(CancelDeliveryCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var delivery = await repository.GetByIdAsync(command.DeliveryId, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException("Livraison", command.DeliveryId.ToString());

        DeliveryAccess.EnsureCanCancel(delivery, caller);

        delivery.Cancel(command.Reason, caller.ToActor(), clock.UtcNow);

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return DeliveryViewMapper.ToView(delivery, caller);
    }
}

public sealed class AdminCloseDeliveryHandler(
    IDeliveryRepository repository,
    IUnitOfWork unitOfWork,
    ICallerContext caller,
    IClock clock) : ICommandHandler<AdminCloseDeliveryCommand, DeliveryView>
{
    public async Task<DeliveryView> HandleAsync(AdminCloseDeliveryCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!caller.Roles.Overlaps(HbaRoles.BackOffice))
        {
            throw new ForbiddenException("Réservé au back-office.");
        }

        if (string.IsNullOrWhiteSpace(command.Reason))
        {
            throw new DomainException("MISSING_REASON", "Une clôture administrative exige un motif : elle est auditée.");
        }

        var delivery = await repository.GetByIdAsync(command.DeliveryId, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException("Livraison", command.DeliveryId.ToString());

        delivery.AdminClose(command.TargetStatus, command.Reason, caller.ToActor(), clock.UtcNow);

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return DeliveryViewMapper.ToView(delivery, caller);
    }
}
