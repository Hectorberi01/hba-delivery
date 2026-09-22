using Hba.BuildingBlocks.Application.Abstractions;
using Hba.BuildingBlocks.Application.Messaging;
using Hba.BuildingBlocks.Domain;
using Hba.BuildingBlocks.Security;
using Hba.Delivery.Application.Ports;
using Hba.Delivery.Application.Views;

namespace Hba.Delivery.Application.Commands.DriverActions;

/// <summary>
/// Socle commun : charger, vérifier que l'appelant est bien LE livreur affecté,
/// appliquer, enregistrer. L'agrégat refait la vérification de son côté.
/// </summary>
public abstract class DriverActionHandlerBase(
    IDeliveryRepository repository,
    IUnitOfWork unitOfWork,
    ICallerContext caller,
    IClock clock)
{
    protected IClock Clock => clock;

    protected ICallerContext Caller => caller;

    protected async Task<DeliveryView> ApplyAsync(
        Guid deliveryId,
        DateTimeOffset? clientTimestamp,
        Action<DeliveryAggregate, Actor, DateTimeOffset> apply,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(apply);

        if (!caller.IsInRole(HbaRoles.Driver))
        {
            throw new ForbiddenException("Cette action appartient au livreur affecté.");
        }

        var delivery = await repository.GetByIdAsync(deliveryId, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException("Livraison", deliveryId.ToString());

        if (delivery.Driver is null || delivery.Driver.DriverId != caller.DriverId)
        {
            // NotFound et non Forbidden : ne rien révéler d'une course qui n'est
            // pas la sienne.
            throw new NotFoundException("Livraison", deliveryId.ToString());
        }

        apply(delivery, caller.ToActor(), NormalizeTimestamp(clientTimestamp));

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return DeliveryViewMapper.ToView(delivery, caller);
    }

    /// <summary>
    /// L'horodatage client est accepté, mais borné : jamais dans le futur, et pas
    /// plus de 24 h dans le passé. Un téléphone mal réglé ne doit pas réécrire
    /// l'histoire d'une course.
    /// </summary>
    private DateTimeOffset NormalizeTimestamp(DateTimeOffset? clientTimestamp)
    {
        var now = clock.UtcNow;

        if (clientTimestamp is null)
        {
            return now;
        }

        var value = clientTimestamp.Value;

        if (value > now || value < now.AddHours(-24))
        {
            return now;
        }

        return value;
    }
}

public sealed class MarkArrivedAtPickupHandler(
    IDeliveryRepository repository,
    IUnitOfWork unitOfWork,
    ICallerContext caller,
    IClock clock)
    : DriverActionHandlerBase(repository, unitOfWork, caller, clock),
      ICommandHandler<MarkArrivedAtPickupCommand, DeliveryView>
{
    public Task<DeliveryView> HandleAsync(MarkArrivedAtPickupCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return ApplyAsync(
            command.DeliveryId,
            command.OccurredAt,
            (delivery, actor, at) => delivery.MarkArrivedAtPickup(actor, at),
            cancellationToken);
    }
}

public sealed class MarkPickedUpHandler(
    IDeliveryRepository repository,
    IUnitOfWork unitOfWork,
    ICallerContext caller,
    IClock clock)
    : DriverActionHandlerBase(repository, unitOfWork, caller, clock),
      ICommandHandler<MarkPickedUpCommand, DeliveryView>
{
    public Task<DeliveryView> HandleAsync(MarkPickedUpCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return ApplyAsync(
            command.DeliveryId,
            command.OccurredAt,
            (delivery, actor, at) => delivery.MarkPickedUp(command.ProofObjectKey, actor, at),
            cancellationToken);
    }
}

public sealed class ConfirmDeliveryHandler(
    IDeliveryRepository repository,
    IUnitOfWork unitOfWork,
    ICallerContext caller,
    IClock clock)
    : DriverActionHandlerBase(repository, unitOfWork, caller, clock),
      ICommandHandler<ConfirmDeliveryCommand, DeliveryView>
{
    public Task<DeliveryView> HandleAsync(ConfirmDeliveryCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return ApplyAsync(
            command.DeliveryId,
            command.OccurredAt,
            (delivery, actor, at) => delivery.ConfirmDelivery(command.Otp, command.ProofObjectKey, actor, at),
            cancellationToken);
    }
}
