using Hba.BuildingBlocks.Messaging.Kafka;
using Hba.Contracts.Common.V1;
using Hba.Contracts.Ride.V1;
using Hba.Realtime.Api.Diffusion;
using Hba.Realtime.Api.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;

namespace Hba.Realtime.Api.Messaging;

/// <summary>
/// Les courses de personnes : ce que le passager voit changer.
/// </summary>
///
/// <remarks>
/// SEPT EVENEMENTS AU CONTRAT, ET TOUS SONT DIFFUSES — contrairement a ce que fait
/// Payment, qui n'en lit que trois. La raison est la nature du consommateur : Payment
/// n'agit que sur ce qui change l'argent, alors qu'un ecran de suivi doit refleter
/// CHAQUE etape, y compris celles qui ne changent rien d'autre que l'attente.
///
/// L'AFFECTATION ET LA CLOTURE FONT PLUS QUE DIFFUSER : elles ecrivent et effacent
/// l'association mission → chauffeur, qui est ce qui autorise le passage des
/// positions. C'est ici que la borne du point 37 s'ouvre et se referme.
///
/// TROIS EVENEMENTS FERMENT LA BORNE, et il faut les trois : terminee, annulee, et
/// aucun chauffeur trouve. Oublier le troisieme laisserait l'association en place
/// pour une course qui n'a jamais eu de chauffeur — sans consequence immediate, mais
/// elle serait reutilisee si ce chauffeur-la etait affecte plus tard a une autre
/// course du meme passager.
/// </remarks>
internal sealed class RideEventsDiffuseur(
    IHubContext<CustomerHub> clients,
    AnnuaireDesMissions annuaire,
    IOptions<KafkaOptions> options,
    ILogger<RideEventsDiffuseur> logger)
    : DiffusionConsumerBase<RideEvent>(options, logger)
{
    private const string Nature = "course";

    protected override string Topic => KafkaTopics.RideEvents;

    protected override async Task DiffuserAsync(RideEvent message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        var (missionId, etat, horodatage) = Lire(message);

        if (string.IsNullOrWhiteSpace(missionId))
        {
            // UN EVENEMENT SANS IDENTIFIANT DE COURSE N'EST PAS DIFFUSABLE : on ne sait a
            // qui l'envoyer. Ce n'est pas une erreur a lever — le topic peut porter demain
            // un evenement que cette version ne connait pas — c'est un message a ignorer.
            return;
        }

        // L'ANNUAIRE AVANT LA DIFFUSION, ET L'ORDRE COMPTE DANS CE SENS-LA.
        //
        // Le client, en recevant « chauffeur affecte », va relire sa course et afficher
        // la carte : s'il s'abonne aux positions dans la seconde qui suit, l'association
        // doit deja etre la. L'inverse ferait clignoter un ecran vide.
        switch (message.PayloadCase)
        {
            case RideEvent.PayloadOneofCase.DriverAssigned:
                await annuaire
                    .AffecterAsync(ServiceType.Ride, missionId, message.DriverAssigned.DriverId)
                    .ConfigureAwait(false);
                break;

            case RideEvent.PayloadOneofCase.Completed:
            case RideEvent.PayloadOneofCase.Cancelled:
            case RideEvent.PayloadOneofCase.NoDriverAvailable:
                // C'EST ICI QUE LE SUIVI S'ETEINT. La position du chauffeur s'arrete a la
                // cloture, en meme temps que son telephone : sans cette ligne, un passager
                // qui garde l'ecran ouvert suivrait le chauffeur chez son client suivant,
                // puis chez lui. Point 37.
                await annuaire.OublierAsync(ServiceType.Ride, missionId).ConfigureAwait(false);
                break;

            default:
                break;
        }

        await clients
            .Clients.Group(Groupes.Mission(ServiceType.Ride, missionId))
            .SendAsync(
                Trames.Methodes.Etat,
                new EtatDeMission(Nature, missionId, etat, horodatage),
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// L'identifiant, l'etat lisible et l'horodatage, selon le cas du oneof.
    /// </summary>
    ///
    /// <remarks>
    /// L'ETAT DIFFUSE EST LE NOM DE L'EVENEMENT, PAS LE STATUT DE L'AGREGAT, et la
    /// nuance est voulue. Les deux se ressemblent mais ne coincident pas : « le chauffeur
    /// est en route » est un evenement dont le statut est DRIVER_ASSIGNED, et un ecran
    /// qui ne verrait que le statut ne saurait pas distinguer « il a accepte » de « il
    /// roule vers toi ». C'est exactement ce que dit le contrat de Ride a propos de
    /// DeclareOnTheWay : cette etape n'est pas decorative.
    ///
    /// L'APPLICATION RELIT LA COURSE DE TOUTE FACON : ce nom lui sert a savoir QUOI
    /// afficher pendant qu'elle relit, pas a remplacer la lecture.
    /// </remarks>
    private static (string MissionId, string Etat, DateTimeOffset? Horodatage) Lire(RideEvent message)
        => message.PayloadCase switch
        {
            RideEvent.PayloadOneofCase.SearchStarted => (
                message.SearchStarted.RideId,
                "SEARCHING_DRIVER",
                Instant(message.SearchStarted.OccurredAt)),

            RideEvent.PayloadOneofCase.DriverAssigned => (
                message.DriverAssigned.RideId,
                "DRIVER_ASSIGNED",
                Instant(message.DriverAssigned.OccurredAt)),

            RideEvent.PayloadOneofCase.DriverArriving => (
                message.DriverArriving.RideId,
                "DRIVER_ARRIVING",
                Instant(message.DriverArriving.OccurredAt)),

            RideEvent.PayloadOneofCase.DriverArrived => (
                message.DriverArrived.RideId,
                "DRIVER_ARRIVED",
                Instant(message.DriverArrived.OccurredAt)),

            RideEvent.PayloadOneofCase.Completed => (
                message.Completed.RideId,
                "COMPLETED",
                Instant(message.Completed.OccurredAt)),

            RideEvent.PayloadOneofCase.Cancelled => (
                message.Cancelled.RideId,
                "CANCELLED",
                Instant(message.Cancelled.OccurredAt)),

            RideEvent.PayloadOneofCase.NoDriverAvailable => (
                message.NoDriverAvailable.RideId,
                "NO_DRIVER_AVAILABLE",
                Instant(message.NoDriverAvailable.OccurredAt)),

            _ => (string.Empty, string.Empty, null),
        };

    private static DateTimeOffset? Instant(Google.Protobuf.WellKnownTypes.Timestamp? quand)
        => quand?.ToDateTimeOffset();
}
