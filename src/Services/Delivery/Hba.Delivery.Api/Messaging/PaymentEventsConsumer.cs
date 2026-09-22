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

            default:
                // Remboursement : traité par Settlement, pas par Delivery.
                break;
        }
    }
}
