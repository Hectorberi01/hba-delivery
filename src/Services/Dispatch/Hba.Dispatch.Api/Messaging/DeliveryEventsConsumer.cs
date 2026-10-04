using Hba.BuildingBlocks.Application.Messaging;
using Hba.BuildingBlocks.Messaging.Kafka;
using Hba.Contracts.Delivery.V1;
using Hba.Dispatch.Application.Features.Dispatching.Commands;
using Hba.Dispatch.Domain.Dispatching;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Hba.Dispatch.Api.Messaging;

/// <summary>
/// Entree du moteur pour les COLIS.
///
/// « DeliveryConfirmed » EST LE SEUL DECLENCHEUR de la recherche d'un livreur
/// pour un colis, comme l'ecrit le flux de livraison. Aucune application, aucun
/// appel direct ne peut ouvrir une recherche : il faut que le paiement soit
/// arrive.
///
/// LES COURSES DE PERSONNES ONT LEUR PROPRE CONSOMMATEUR depuis le 1er octobre
/// 2026, et il envoie les memes commandes avec ServiceType.Ride. Deux
/// consommateurs plutot qu'un parce que les deux topics portent des contrats
/// differents : un seul lirait deux formes de messages sans que le type ne le
/// dise.
/// </summary>
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

        var dispatcherTot = services.GetRequiredService<IDispatcher>();

        // UNE COURSE CLOSE ARRETE LE MOTEUR. Sans cela il continuerait ses
        // vagues sur une livraison annulee : des livreurs se deplaceraient
        // pour rien, et chaque offre publiee ferait echouer le consumer de
        // Delivery sur un etat terminal.
        if (message.PayloadCase is DeliveryEvent.PayloadOneofCase.Cancelled
            or DeliveryEvent.PayloadOneofCase.Failed)
        {
            var identifiant = message.PayloadCase == DeliveryEvent.PayloadOneofCase.Cancelled
                ? message.Cancelled.DeliveryId
                : message.Failed.DeliveryId;

            if (Guid.TryParse(identifiant, out var closeId))
            {
                await dispatcherTot
                    .SendAsync(
                        new CancelDispatchCommand(
                            ServiceType.Delivery, closeId, message.PayloadCase.ToString()),
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            return;
        }

        if (message.PayloadCase != DeliveryEvent.PayloadOneofCase.Confirmed)
        {
            return;
        }

        var confirmed = message.Confirmed;

        if (!Guid.TryParse(confirmed.DeliveryId, out var deliveryId) || confirmed.Pickup?.Point is null)
        {
            logger.LogWarning(
                "DeliveryConfirmed inexploitable pour {DeliveryId} : identifiant ou point d'enlevement manquant.",
                confirmed.DeliveryId);
            return;
        }

        await dispatcherTot.SendAsync(
            new StartDispatchCommand(
                ServiceType.Delivery,
                deliveryId,
                confirmed.Pickup.Point.Latitude,
                confirmed.Pickup.Point.Longitude,
                confirmed.Pickup.Landmark,
                confirmed.DistanceMeters,
                confirmed.DriverEarning?.Amount ?? 0),
            cancellationToken).ConfigureAwait(false);
    }
}
