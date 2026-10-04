using Hba.BuildingBlocks.Application.Messaging;
using Hba.BuildingBlocks.Messaging.Kafka;
using Hba.Contracts.Delivery.V1;
using Hba.Rating.Application.Features.Reviews;
using Hba.Rating.Domain.Reviews;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Hba.Rating.Api.Messaging;

/// <summary>
/// Un colis remis ouvre sa notation.
/// </summary>
///
/// <remarks>
/// SEULEMENT LA REMISE. Une livraison annulee ou echouee ne se note pas : le
/// client n'a pas recu son colis, et ce qu'il aurait a dire releve de la
/// reclamation, pas de l'avis. Ce sont deux chemins differents, et les melanger
/// ferait d'une note basse le seul moyen de signaler un probleme.
///
/// LE POINT 42 N'OUVRE PAS LE POURBOIRE AUX COLIS, mais la notation, elle, n'a
/// jamais ete limitee a la course de personnes : le point 41 parle de livreurs,
/// et un colis en a un.
/// </remarks>
public sealed class DeliveryEventsConsumer(
    IServiceScopeFactory scopeFactory,
    IOptions<KafkaOptions> options,
    ILogger<DeliveryEventsConsumer> logger)
    : KafkaConsumerBase<DeliveryEvent>(scopeFactory, options, logger)
{
    protected override string Topic => KafkaTopics.DeliveryEvents;

    protected override Guid GetEventId(DeliveryEvent message)
    {
        ArgumentNullException.ThrowIfNull(message);
        return Guid.TryParse(message.Envelope?.EventId, out var id) ? id : Guid.CreateVersion7();
    }

    protected override string GetEventType(DeliveryEvent message)
    {
        ArgumentNullException.ThrowIfNull(message);
        return message.Envelope?.EventType ?? "unknown";
    }

    protected override async Task HandleAsync(
        DeliveryEvent message,
        IServiceProvider services,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(services);

        if (message.PayloadCase != DeliveryEvent.PayloadOneofCase.Delivered)
        {
            return;
        }

        var remise = message.Delivered;

        if (!Guid.TryParse(remise.DeliveryId, out var deliveryId)
            || !Guid.TryParse(remise.DriverId, out var driverId)
            || string.IsNullOrWhiteSpace(remise.CustomerId))
        {
            return;
        }

        var dispatcher = services.GetRequiredService<IDispatcher>();

        await dispatcher
            .SendAsync(
                new OpenReviewCommand(
                    MissionType.Delivery,
                    deliveryId,
                    remise.CustomerId,
                    driverId,
                    remise.OccurredAt?.ToDateTimeOffset() ?? DateTimeOffset.UtcNow),
                cancellationToken)
            .ConfigureAwait(false);
    }
}
