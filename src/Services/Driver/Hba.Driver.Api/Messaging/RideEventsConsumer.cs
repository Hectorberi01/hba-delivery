using Hba.BuildingBlocks.Application.Messaging;
using Hba.BuildingBlocks.Domain;
using Hba.BuildingBlocks.Messaging.Kafka;
using Hba.Contracts.Common.V1;
using Hba.Contracts.Ride.V1;
using Hba.Driver.Application.Features.Drivers.Commands;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Hba.Driver.Api.Messaging;

/// <summary>
/// La fin d'une COURSE DE PERSONNES rend le chauffeur disponible.
///
/// IL MANQUAIT, ET SON ABSENCE SE VOYAIT A L'ECRAN. Driver ecoutait Dispatch —
/// qui sait qui a GAGNE une mission — et Delivery — qui sait quand un COLIS
/// arrive. Depuis que les courses de personnes existent, le 1er octobre 2026,
/// une mission sur deux s'achevait sans que personne ne le dise a Driver : le
/// chauffeur montait a ON_MISSION et n'en redescendait jamais.
///
/// CE QUE CELA DONNAIT, LE 3 OCTOBRE 2026. L'application livreur affichait
/// « En ligne » et un ecran d'accueil vide — aucune course en cours, huit
/// closes sur huit — pendant que son profil disait « EN MISSION » et que la
/// carte du client le montrait eteint. Les trois ecrans avaient raison
/// separement : c'est l'etat en base qui etait faux.
///
/// LA SYMETRIE EST LA LECON. Toute nature de mission qui peut faire monter un
/// chauffeur a ON_MISSION doit avoir ici de quoi l'en faire redescendre. Une
/// troisieme nature — un jour, une livraison de repas qui ne passerait pas par
/// Delivery — redemanderait un consommateur de plus.
///
/// LES DEUX FINS COMPTENT AUTANT. Une course achevee ou annulee laisse dans les
/// deux cas un chauffeur qui n'a plus rien a porter. Il n'y a pas de troisieme
/// fin cote course : l'echec d'une course de personnes n'existe pas au contrat.
/// </summary>
public sealed class RideEventsConsumer(
    IServiceScopeFactory scopeFactory,
    IOptions<KafkaOptions> options,
    ILogger<RideEventsConsumer> logger)
    : KafkaConsumerBase<RideEvent>(scopeFactory, options, logger)
{
    protected override string Topic => KafkaTopics.RideEvents;

    protected override Guid GetEventId(RideEvent message)
    {
        ArgumentNullException.ThrowIfNull(message);
        return Guid.TryParse(message.Envelope?.EventId, out var id) ? id : Guid.CreateVersion7();
    }

    protected override string GetEventType(RideEvent message)
    {
        ArgumentNullException.ThrowIfNull(message);
        return message.Envelope?.EventType ?? "unknown";
    }

    protected override async Task HandleAsync(
        RideEvent message,
        IServiceProvider services,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(services);

        var driverId = message.PayloadCase switch
        {
            RideEvent.PayloadOneofCase.Completed => message.Completed.DriverId,
            RideEvent.PayloadOneofCase.Cancelled => message.Cancelled.DriverId,
            _ => string.Empty,
        };

        // UN IDENTIFIANT VIDE EST LE CAS NORMAL, PAS UNE ANOMALIE : une course
        // annulee avant qu'un chauffeur n'accepte n'a personne a liberer.
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
    /// L'AUDIT DOIT DIRE QUI A MIS FIN A LA COURSE, et ce n'est pas toujours la
    /// meme personne : le chauffeur quand il cloture, le passager ou
    /// l'administrateur quand ils annulent. Inventer un acteur unique serait
    /// commode et faux.
    ///
    /// C'EST LE MEME CODE QUE DANS DeliveryEventsConsumer, ET IL N'A PAS ETE MIS
    /// EN COMMUN. Les deux lisent la meme enveloppe, mais chacun depend du
    /// contrat de SON service : les factoriser ferait de ce fichier le point ou
    /// Delivery et Ride se rencontrent, pour economiser quinze lignes.
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
            _ => Actor.Driver(driverId),
        };
    }
}
