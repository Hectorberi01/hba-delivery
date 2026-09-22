using Hba.BuildingBlocks.Application.Abstractions;
using Hba.BuildingBlocks.Application.Messaging;
using Hba.BuildingBlocks.Domain;
using Hba.Delivery.Application.Ports;
using Microsoft.Extensions.Logging;

namespace Hba.Delivery.Application.Commands.Internal;

public sealed class ConfirmPaymentHandler(
    IDeliveryRepository repository,
    IUnitOfWork unitOfWork,
    ILogger<ConfirmPaymentHandler> logger) : ICommandHandler<ConfirmPaymentCommand, Unit>
{
    public async Task<Unit> HandleAsync(ConfirmPaymentCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var delivery = await repository.GetByIdAsync(command.DeliveryId, cancellationToken).ConfigureAwait(false);

        if (delivery is null)
        {
            // Le paiement concerne une livraison inconnue : ne pas bloquer la
            // partition, mais le signaler — c'est une incohérence à instruire.
            logger.LogError(
                "Paiement confirmé pour une livraison inconnue : {DeliveryId} ({PaymentIntentId}).",
                command.DeliveryId,
                command.PaymentIntentId);

            return Unit.Value;
        }

        delivery.ConfirmPayment(command.PaymentIntentId, Actor.FedaPay, command.PaidAt);

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Unit.Value;
    }
}

public sealed class FailPaymentHandler(
    IDeliveryRepository repository,
    IUnitOfWork unitOfWork) : ICommandHandler<FailPaymentCommand, Unit>
{
    public async Task<Unit> HandleAsync(FailPaymentCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var delivery = await repository.GetByIdAsync(command.DeliveryId, cancellationToken).ConfigureAwait(false);
        if (delivery is null)
        {
            return Unit.Value;
        }

        delivery.FailPayment(command.Reason, Actor.FedaPay, command.OccurredAt);

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Unit.Value;
    }
}

public sealed class StartDriverSearchHandler(
    IDeliveryRepository repository,
    IUnitOfWork unitOfWork) : ICommandHandler<StartDriverSearchCommand, Unit>
{
    public async Task<Unit> HandleAsync(StartDriverSearchCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var delivery = await repository.GetByIdAsync(command.DeliveryId, cancellationToken).ConfigureAwait(false);
        if (delivery is null)
        {
            return Unit.Value;
        }

        delivery.StartDriverSearch(Actor.DispatchEngine, command.OccurredAt);

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Unit.Value;
    }
}

public sealed class AssignDriverHandler(
    IDeliveryRepository repository,
    IDriverDirectory drivers,
    IUnitOfWork unitOfWork) : ICommandHandler<AssignDriverCommand, Unit>
{
    public async Task<Unit> HandleAsync(AssignDriverCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var delivery = await repository.GetByIdAsync(command.DeliveryId, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException("Livraison", command.DeliveryId.ToString());

        // Le dispatch peut accepter une offre avant que Delivery ait traité la
        // première vague : on ouvre la recherche si besoin, plutôt que d'échouer.
        if (delivery.Status == Domain.Deliveries.DeliveryStatus.Paid)
        {
            delivery.StartDriverSearch(Actor.DispatchEngine, command.AssignedAt);
        }

        var profile = await drivers.GetPublicProfileAsync(command.DriverId, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException("Livreur", command.DriverId);

        delivery.AssignDriver(profile, command.OfferId, Actor.DispatchEngine, command.AssignedAt);

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Unit.Value;
    }
}

public sealed class MarkNoDriverFoundHandler(
    IDeliveryRepository repository,
    IUnitOfWork unitOfWork) : ICommandHandler<MarkNoDriverFoundCommand, Unit>
{
    public async Task<Unit> HandleAsync(MarkNoDriverFoundCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var delivery = await repository.GetByIdAsync(command.DeliveryId, cancellationToken).ConfigureAwait(false);
        if (delivery is null)
        {
            return Unit.Value;
        }

        delivery.MarkNoDriverFound(command.WavesAttempted, Actor.DispatchEngine, command.OccurredAt);

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Unit.Value;
    }
}

public sealed class UnassignDriverHandler(
    IDeliveryRepository repository,
    IUnitOfWork unitOfWork) : ICommandHandler<UnassignDriverCommand, Unit>
{
    public async Task<Unit> HandleAsync(UnassignDriverCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var delivery = await repository.GetByIdAsync(command.DeliveryId, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException("Livraison", command.DeliveryId.ToString());

        delivery.Unassign(command.Reason, Actor.Admin(command.AdminId), command.OccurredAt);

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Unit.Value;
    }
}
