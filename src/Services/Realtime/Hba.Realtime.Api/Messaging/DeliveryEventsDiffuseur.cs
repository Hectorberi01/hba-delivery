using Hba.BuildingBlocks.Messaging.Kafka;
using Hba.Contracts.Common.V1;
using Hba.Contracts.Delivery.V1;
using Hba.Realtime.Api.Diffusion;
using Hba.Realtime.Api.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;

namespace Hba.Realtime.Api.Messaging;

/// <summary>
/// Les livraisons de colis : ce que le client voit changer.
/// </summary>
///
/// <remarks>
/// MEME FORME QUE POUR LES COURSES, ET DEUX DIFFERENCES DE FOND.
///
/// 1. « CREE » N'EST PAS DIFFUSE. Une livraison creee n'est pas encore payee, donc
///    pas encore confirmee : le client est sur son ecran de paiement, pas sur son
///    ecran de suivi. Lui pousser un etat a ce moment-la ne servirait a rien et
///    arriverait avant que l'ecran qui l'ecoute existe.
///
/// 2. QUATRE EVENEMENTS FERMENT LA BORNE au lieu de trois : terminee, annulee,
///    ECHOUEE, et aucun livreur trouve. L'echec n'existe pas pour une course de
///    personnes — un passager monte ou ne monte pas — alors qu'un colis peut etre
///    refuse a la remise. Oublier celui-la laisserait un client suivre indefiniment
///    le livreur d'un colis qu'il n'a jamais recu.
/// </remarks>
internal sealed class DeliveryEventsDiffuseur(
    IHubContext<CustomerHub> clients,
    AnnuaireDesMissions annuaire,
    IOptions<KafkaOptions> options,
    ILogger<DeliveryEventsDiffuseur> logger)
    : DiffusionConsumerBase<DeliveryEvent>(options, logger)
{
    private const string Nature = "livraison";

    protected override string Topic => KafkaTopics.DeliveryEvents;

    protected override async Task DiffuserAsync(DeliveryEvent message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        var (missionId, etat, horodatage) = Lire(message);

        if (string.IsNullOrWhiteSpace(missionId))
        {
            return;
        }

        switch (message.PayloadCase)
        {
            case DeliveryEvent.PayloadOneofCase.DriverAssigned:
                await annuaire
                    .AffecterAsync(ServiceType.Delivery, missionId, message.DriverAssigned.DriverId)
                    .ConfigureAwait(false);
                break;

            case DeliveryEvent.PayloadOneofCase.Delivered:
            case DeliveryEvent.PayloadOneofCase.Cancelled:
            case DeliveryEvent.PayloadOneofCase.Failed:
            case DeliveryEvent.PayloadOneofCase.NoDriverFound:
                // LA BORNE DU POINT 37 SE REFERME ICI. Sans cette ligne, un client qui
                // garde son ecran ouvert suivrait le livreur chez son client suivant.
                await annuaire.OublierAsync(ServiceType.Delivery, missionId).ConfigureAwait(false);
                break;

            default:
                break;
        }

        await clients
            .Clients.Group(Groupes.Mission(ServiceType.Delivery, missionId))
            .SendAsync(
                Trames.Methodes.Etat,
                new EtatDeMission(Nature, missionId, etat, horodatage),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static (string MissionId, string Etat, DateTimeOffset? Horodatage) Lire(DeliveryEvent message)
        => message.PayloadCase switch
        {
            // « CONFIRMEE » EST LE PREMIER ETAT QUI INTERESSE L'ECRAN DE SUIVI : c'est
            // celui ou le paiement est passe et ou la recherche d'un livreur commence.
            DeliveryEvent.PayloadOneofCase.Confirmed => (
                message.Confirmed.DeliveryId,
                "CONFIRMED",
                Instant(message.Confirmed.PaidAt)),

            DeliveryEvent.PayloadOneofCase.DriverAssigned => (
                message.DriverAssigned.DeliveryId,
                "DRIVER_ASSIGNED",
                Instant(message.DriverAssigned.AssignedAt)),

            DeliveryEvent.PayloadOneofCase.DriverArrivedAtPickup => (
                message.DriverArrivedAtPickup.DeliveryId,
                "DRIVER_AT_PICKUP",
                Instant(message.DriverArrivedAtPickup.OccurredAt)),

            DeliveryEvent.PayloadOneofCase.PickedUp => (
                message.PickedUp.DeliveryId,
                "PICKED_UP",
                Instant(message.PickedUp.OccurredAt)),

            DeliveryEvent.PayloadOneofCase.Delivered => (
                message.Delivered.DeliveryId,
                "DELIVERED",
                Instant(message.Delivered.OccurredAt)),

            DeliveryEvent.PayloadOneofCase.Cancelled => (
                message.Cancelled.DeliveryId,
                "CANCELLED",
                Instant(message.Cancelled.OccurredAt)),

            DeliveryEvent.PayloadOneofCase.Failed => (
                message.Failed.DeliveryId,
                "FAILED",
                Instant(message.Failed.OccurredAt)),

            DeliveryEvent.PayloadOneofCase.NoDriverFound => (
                message.NoDriverFound.DeliveryId,
                "NO_DRIVER_FOUND",
                Instant(message.NoDriverFound.OccurredAt)),

            _ => (string.Empty, string.Empty, null),
        };

    private static DateTimeOffset? Instant(Google.Protobuf.WellKnownTypes.Timestamp? quand)
        => quand?.ToDateTimeOffset();
}
