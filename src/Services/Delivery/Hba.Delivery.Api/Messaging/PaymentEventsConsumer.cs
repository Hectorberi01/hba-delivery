using Hba.BuildingBlocks.Application.Messaging;
using Hba.BuildingBlocks.Messaging.Kafka;
using Hba.Contracts.Common.V1;
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

        // CE QUI N'EST PAS UNE LIVRAISON NE NOUS CONCERNE PAS, ET CE TEST EST
        // OBLIGATOIRE DEPUIS LE 1er OCTOBRE 2026.
        //
        // Payment regle aussi des COURSES DE PERSONNES, sur le meme topic. Sans ce
        // filtre, l'encaissement d'une course arriverait ici avec son identifiant
        // de course : ConfirmPaymentCommand chercherait une livraison qui n'existe
        // pas, leverait NotFoundException, le message serait reessaye trois fois
        // puis abandonne — sur un paiement pourtant encaisse, et une course qui ne
        // partirait jamais.
        //
        // UNE NATURE NON PRECISEE EST AUSSI IGNOREE : elle signale un producteur
        // fautif, et agir dessus serait deviner.
        if (Nature(message) != ServiceType.Delivery)
        {
            return;
        }

        var dispatcher = services.GetRequiredService<IDispatcher>();

        switch (message.PayloadCase)
        {
            case PaymentEvent.PayloadOneofCase.Succeeded:
                var succeeded = message.Succeeded;
                if (Guid.TryParse(succeeded.ReferenceId, out var paidId))
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
                if (Guid.TryParse(failed.ReferenceId, out var failedId))
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
                if (Guid.TryParse(refunded.ReferenceId, out var refundedId))
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

    /// <summary>
    /// La nature de la mission portee par le message, quel que soit son payload.
    ///
    /// CHAQUE PAYLOAD PORTE SON PROPRE CHAMP, parce que le contrat met la nature
    /// dans les messages et non dans l'enveloppe : l'enveloppe decrit l'EMETTEUR —
    /// Payment, dans tous les cas — pas la mission.
    /// </summary>
    private static ServiceType Nature(PaymentEvent message) => message.PayloadCase switch
    {
        PaymentEvent.PayloadOneofCase.Succeeded => message.Succeeded.ServiceType,
        PaymentEvent.PayloadOneofCase.Failed => message.Failed.ServiceType,
        PaymentEvent.PayloadOneofCase.Refunded => message.Refunded.ServiceType,
        _ => ServiceType.Unspecified,
    };
}
