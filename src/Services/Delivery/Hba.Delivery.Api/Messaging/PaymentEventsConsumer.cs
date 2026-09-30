using Hba.BuildingBlocks.Application.Messaging;
using Hba.BuildingBlocks.Messaging.Kafka;
using Hba.Contracts.Payment.V1;
using Hba.Delivery.Application.Commands.Internal;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Hba.Delivery.Api.Messaging;

/// <summary>
/// Entrée asynchrone : le paiement. C'est le seul chemin par lequel une
/// livraison passe en Paid — jamais un appel direct d'une application.
/// </summary>
public sealed class PaymentEventsConsumer(
    IServiceScopeFactory scopeFactory,
    IOptions<KafkaOptions> options,
    ILogger<PaymentEventsConsumer> logger)
    : KafkaConsumerBase<PaymentEvent>(scopeFactory, options, logger)
{
    protected override string Topic => KafkaTopics.PaymentEvents;

    protected override Guid GetEventId(PaymentEvent message)
    {
        ArgumentNullException.ThrowIfNull(message);
        return Guid.TryParse(message.Envelope?.EventId, out var id) ? id : Guid.CreateVersion7();
    }

    protected override string GetEventType(PaymentEvent message)
    {
        ArgumentNullException.ThrowIfNull(message);
        return message.Envelope?.EventType ?? "unknown";
    }

    protected override async Task HandleAsync(
        PaymentEvent message,
        IServiceProvider services,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(services);

        var dispatcher = services.GetRequiredService<IDispatcher>();

        switch (message.PayloadCase)
        {
            case PaymentEvent.PayloadOneofCase.Succeeded:
                var succeeded = message.Succeeded;
                if (Guid.TryParse(succeeded.DeliveryId, out var paidId))
                {
                    await dispatcher.SendAsync(
                        new ConfirmPaymentCommand(
                            paidId,
                            succeeded.PaymentIntentId,
                            succeeded.SucceededAt?.ToDateTimeOffset() ?? DateTimeOffset.UtcNow),
                        cancellationToken).ConfigureAwait(false);
                }

                break;

            case PaymentEvent.PayloadOneofCase.Failed:
                var failed = message.Failed;
                if (Guid.TryParse(failed.DeliveryId, out var failedId))
                {
                    await dispatcher.SendAsync(
                        new FailPaymentCommand(
                            failedId,
                            failed.Reason,
                            failed.OccurredAt?.ToDateTimeOffset() ?? DateTimeOffset.UtcNow),
                        cancellationToken).ConfigureAwait(false);
                }

                break;

            // LE REMBOURSEMENT EST CONSTATE ICI, ET IL NE L'ETAIT NULLE PART.
            //
            // Le commentaire disait « traite par Settlement » : il n'existe pas
            // de service Settlement dans ce depot, et « PaymentRefunded »
            // n'avait ni producteur ni consommateur. Le client lisait « HBA
            // revient vers vous au sujet du montant preleve » et rien, jamais,
            // ne venait dire que c'etait fait.
            //
            // AUCUN CHANGEMENT D'ETAT : la course reste annulee, echouee ou sans
            // livreur. On note la date, et l'ecran peut enfin le dire.
            case PaymentEvent.PayloadOneofCase.Refunded:
                var refunded = message.Refunded;
                if (Guid.TryParse(refunded.DeliveryId, out var refundedId))
                {
                    await dispatcher.SendAsync(
                        new MarkDeliveryRefundedCommand(
                            refundedId,
                            refunded.Partial,
                            refunded.OccurredAt?.ToDateTimeOffset() ?? DateTimeOffset.UtcNow),
                        cancellationToken).ConfigureAwait(false);
                }

                break;

            default:
                // Rien d'autre ne concerne Delivery.
                break;
        }
    }
}
