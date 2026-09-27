using Hba.BuildingBlocks.Application.Messaging;
using Hba.BuildingBlocks.Messaging.Kafka;
using Hba.Contracts.Delivery.V1;
using Hba.Payment.Application.Features.Earnings.Commands;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Hba.Payment.Api.Messaging;

/// <summary>
/// Une course livree credite le compte de son livreur.
///
/// POURQUOI PAYMENT, ET PAS DRIVER NI DELIVERY. Le referentiel confie a
/// « finance » les paiements, les remboursements ET les reversements : les
/// trois sont le meme metier, traite par la meme personne, et Payment les
/// tient deja tous les deux premiers. Driver sait qui roule, pas ce qu'on lui
/// doit ; Delivery sait ce qu'une course vaut, pas ce qui reste a verser.
///
/// PREMIER CONSOMMATEUR DE CE SERVICE. Payment ne faisait jusqu'ici que
/// publier — il ecoute maintenant, et son groupe de consommateurs n'ayant
/// jamais lu ce sujet, il rattrape l'historique depuis l'origine. C'est
/// voulu : les courses deja livrees doivent apparaitre au compte de leur
/// livreur, pas seulement les prochaines.
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

        // SEULE LA REMISE CREDITE. Une course annulee ou echouee ne rapporte
        // rien : si un dedommagement doit exister un jour, ce sera une regle
        // a trancher et une autre nature de mouvement, pas un « presque
        // livree » glisse ici.
        if (message.PayloadCase != DeliveryEvent.PayloadOneofCase.Delivered)
        {
            return;
        }

        var livree = message.Delivered;

        if (!Guid.TryParse(livree.DeliveryId, out var deliveryId))
        {
            return;
        }

        var dispatcher = services.GetRequiredService<IDispatcher>();

        await dispatcher
            .SendAsync(
                new RecordDeliveryEarningCommand(
                    deliveryId,
                    livree.DriverId,
                    livree.Reference,
                    livree.DriverEarning?.Amount ?? 0,
                    livree.OccurredAt?.ToDateTimeOffset() ?? DateTimeOffset.UtcNow),
                cancellationToken)
            .ConfigureAwait(false);
    }
}
