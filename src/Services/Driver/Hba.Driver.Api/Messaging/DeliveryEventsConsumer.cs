using Hba.BuildingBlocks.Application.Messaging;
using Hba.BuildingBlocks.Domain;
using Hba.BuildingBlocks.Messaging.Kafka;
using Hba.Contracts.Common.V1;
using Hba.Contracts.Delivery.V1;
using Hba.Driver.Application.Features.Drivers.Commands;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Hba.Driver.Api.Messaging;

/// <summary>
/// La fin d'une course rend le livreur disponible.
///
/// POURQUOI DRIVER ECOUTE DELIVERY, ALORS QU'IL N'ECOUTAIT QUE DISPATCH.
/// Dispatch sait qui a GAGNE une course ; il ne sait pas quand elle
/// s'acheve — ni par une remise, ni par une annulation. Sans ce
/// consommateur, l'etat operationnel du livreur montait jusqu'a ON_MISSION
/// et n'en redescendait jamais.
///
/// LES TROIS FINS COMPTENT AUTANT. Une course livree, annulee ou echouee
/// laisse dans les trois cas un livreur qui n'a plus rien a porter.
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

        var driverId = message.PayloadCase switch
        {
            DeliveryEvent.PayloadOneofCase.Delivered => message.Delivered.DriverId,
            DeliveryEvent.PayloadOneofCase.Cancelled => message.Cancelled.DriverId,
            DeliveryEvent.PayloadOneofCase.Failed => message.Failed.DriverId,
            _ => string.Empty,
        };

        // UN IDENTIFIANT VIDE EST LE CAS NORMAL, PAS UNE ANOMALIE : une course
        // annulee avant d'avoir trouve preneur n'a aucun livreur a liberer.
        if (!Guid.TryParse(driverId, out var id))
        {
            return;
        }

        var dispatcher = services.GetRequiredService<IDispatcher>();

        await dispatcher
            .SendAsync(new EndDriverMissionCommand(id, Auteur(message.Envelope, driverId)), cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Reconstruit l'auteur depuis l'enveloppe.
    ///
    /// L'AUDIT DOIT DIRE QUI A MIS FIN A LA COURSE, et ce n'est pas toujours
    /// la meme personne : le livreur quand il prouve la remise, le client ou
    /// l'administrateur quand ils annulent. Inventer un acteur unique —
    /// « le moteur de dispatch », par exemple — serait commode et faux : le
    /// dispatch n'a rien a voir avec la fin d'une course.
    ///
    /// L'ENVELOPPE PORTE DEJA CETTE INFORMATION, puisque Delivery l'y ecrit a
    /// la publication. On la relit plutot que de la deviner.
    /// </summary>
    private static Actor Auteur(EventEnvelope? envelope, string driverId)
    {
        var id = string.IsNullOrWhiteSpace(envelope?.ActorId) ? driverId : envelope!.ActorId;

        return envelope?.ActorType switch
        {
            ActorType.Customer => Actor.Customer(id),
            ActorType.Driver => Actor.Driver(id),
            ActorType.Merchant => Actor.Merchant(id),
            ActorType.Partner => Actor.Partner(id),
            ActorType.Admin => Actor.Admin(id),
            ActorType.Dispatch => Actor.DispatchEngine,
            ActorType.PaymentProvider => Actor.FedaPay,
            ActorType.Scheduler => Actor.Scheduler,

            // UNE ENVELOPPE SANS ACTEUR NE DOIT PAS EMPECHER LA LIBERATION.
            // Le livreur qui portait la course est alors l'attribution la
            // moins fausse : c'est sa mission qui se termine.
            _ => Actor.Driver(driverId),
        };
    }
}
