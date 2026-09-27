using Hba.BuildingBlocks.Application.Messaging;
using Hba.BuildingBlocks.Messaging.Kafka;
using Hba.Contracts.Dispatch.V1;
using Hba.Delivery.Application.Commands.Internal;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Hba.Delivery.Api.Messaging;

/// <summary>
/// Entrée asynchrone : le dispatch. Un seul consommateur pour tout le topic,
/// plutôt qu'un par type d'événement : l'ordre par livraison est ainsi préservé,
/// la clé de partition étant l'identifiant de livraison.
/// </summary>
public sealed class DispatchEventsConsumer(
    IServiceScopeFactory scopeFactory,
    IOptions<KafkaOptions> options,
    ILogger<DispatchEventsConsumer> logger)
    : KafkaConsumerBase<DispatchEvent>(scopeFactory, options, logger)
{
    protected override string Topic => KafkaTopics.DispatchEvents;

    protected override Guid GetEventId(DispatchEvent message)
    {
        ArgumentNullException.ThrowIfNull(message);
        return Guid.TryParse(message.Envelope?.EventId, out var id) ? id : Guid.CreateVersion7();
    }

    protected override string GetEventType(DispatchEvent message)
    {
        ArgumentNullException.ThrowIfNull(message);
        return message.Envelope?.EventType ?? "unknown";
    }

    protected override async Task HandleAsync(
        DispatchEvent message,
        IServiceProvider services,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(services);

        var dispatcher = services.GetRequiredService<IDispatcher>();

        switch (message.PayloadCase)
        {
            // Une offre est partie : la livraison passe en recherche.
            //
            // N'IMPORTE QUELLE VAGUE, PAS SEULEMENT LA PREMIERE. Une première
            // vague sans candidat ne publie aucune offre : si seule la vague 1
            // ouvrait la recherche, une course trouvée au deuxième rayon
            // resterait affichée « payée » au client pendant que des livreurs
            // la regardent déjà. La transition est idempotente — l'agrégat
            // sort tout de suite s'il est déjà en recherche.
            case DispatchEvent.PayloadOneofCase.OfferSent:
                var sent = message.OfferSent;
                if (Guid.TryParse(sent.DeliveryId, out var searchingId))
                {
                    await dispatcher.SendAsync(
                        new StartDriverSearchCommand(
                            searchingId,
                            sent.ExpiresAt?.ToDateTimeOffset() ?? DateTimeOffset.UtcNow),
                        cancellationToken).ConfigureAwait(false);
                }

                break;

            // Le gagnant est déjà départagé côté Dispatch : ici, on enregistre.
            case DispatchEvent.PayloadOneofCase.OfferAccepted:
                var accepted = message.OfferAccepted;
                if (Guid.TryParse(accepted.DeliveryId, out var assignedId))
                {
                    await dispatcher.SendAsync(
                        new AssignDriverCommand(
                            assignedId,
                            accepted.DriverId,
                            accepted.OfferId,
                            accepted.AcceptedAt?.ToDateTimeOffset() ?? DateTimeOffset.UtcNow),
                        cancellationToken).ConfigureAwait(false);
                }

                break;

            case DispatchEvent.PayloadOneofCase.DispatchExhausted:
                var exhausted = message.DispatchExhausted;
                if (Guid.TryParse(exhausted.DeliveryId, out var noDriverId))
                {
                    await dispatcher.SendAsync(
                        new MarkNoDriverFoundCommand(
                            noDriverId,
                            exhausted.WavesAttempted,
                            exhausted.OccurredAt?.ToDateTimeOffset() ?? DateTimeOffset.UtcNow),
                        cancellationToken).ConfigureAwait(false);
                }

                break;

            default:
                // Offre expirée : sans effet sur la livraison, le dispatch relance.
                break;
        }
    }
}
