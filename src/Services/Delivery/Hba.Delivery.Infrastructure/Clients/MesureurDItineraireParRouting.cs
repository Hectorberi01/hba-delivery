using Grpc.Core;
using Hba.BuildingBlocks.Grpc.ServiceAuth;
using Hba.Contracts.Common.V1;
using Hba.Contracts.Location.V1;
using Hba.Contracts.Routing.V1;
using Hba.Delivery.Application.Ports;
using Microsoft.Extensions.Logging;

namespace Hba.Delivery.Infrastructure.Clients;

/// <summary>
/// Deux appels : où est le livreur (Location), puis combien de temps il lui faut
/// (Routing).
/// </summary>
///
/// <remarks>
/// POURQUOI DEUX SERVICES ET NON UN. Location sait où sont les livreurs et
/// IGNORE par où passent les rues — son propre contrat le dit. Routing mesure
/// des trajets et ne sait pas qui conduit. Les joindre dans l'un des deux aurait
/// donné à un service une connaissance qui n'est pas la sienne ; les joindre ICI
/// coûte un aller-retour de plus et ne coûte que cela.
///
/// AUCUNE ERREUR NE REMONTE AU CLIENT. Ce calcul est un AGRÉMENT : il enrichit
/// la lecture d'une livraison, il ne la conditionne pas. Un Routing en panne
/// doit faire disparaître une durée, jamais faire échouer l'affichage d'une
/// course en cours. Toute panne rend donc nul, et l'application retombe sur la
/// distance qu'elle sait calculer seule.
///
/// LE JETON DE SERVICE EST OBLIGATOIRE : Location et Routing sont en
/// [Authorize], et l'appel part d'une requête client dont le jeton ne vaut pas
/// pour eux. Voir ADR 0018.
/// </remarks>
internal sealed class MesureurDItineraireParRouting(
    LocationService.LocationServiceClient location,
    RoutingService.RoutingServiceClient routing,
    IServiceTokenProvider tokens,
    TimeProvider horloge,
    ILogger<MesureurDItineraireParRouting> logger) : IMesureurDItineraire
{
    public async Task<RouteActive?> MesurerAsync(
        string driverId,
        double cibleLatitude,
        double cibleLongitude,
        string profil,
        bool avecTrace,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(driverId))
        {
            return null;
        }

        try
        {
            var headers = await tokens.AuthorizationAsync(cancellationToken).ConfigureAwait(false);

            var lookup = await location.GetPositionAsync(
                new GetPositionRequest { DriverId = driverId },
                headers: headers,
                cancellationToken: cancellationToken);

            // « FOUND » FAUX N'EST PAS UNE ERREUR, C'EST LA RÉPONSE. Le livreur
            // n'a pas donné signe de vie dans la fenêtre de fraîcheur : estimer
            // depuis une position d'il y a une heure annoncerait une arrivée qui
            // n'a aucun rapport avec l'endroit où il est.
            if (!lookup.Found || lookup.Position?.Point is null)
            {
                return null;
            }

            var route = await routing.CalculateRouteAsync(
                new CalculateRouteRequest
                {
                    Origin = lookup.Position.Point,
                    Destination = new GeoPoint
                    {
                        Latitude = cibleLatitude,
                        Longitude = cibleLongitude,
                    },
                    Profile = Profil(profil),

                    // LE TRACÉ NE PART QUE S'IL SERA DESSINÉ. Il pèse plusieurs
                    // kilo-octets par appel, et le client béninois paie ses
                    // données au mégaoctet : l'envoyer à un bandeau qui affiche
                    // quatre mots serait une dépense prise sur lui.
                    IncludePolyline = avecTrace,
                },
                headers: headers,
                cancellationToken: cancellationToken);

            return new RouteActive(
                route.DistanceMeters,
                route.DurationSeconds,
                route.FromFallback,
                horloge.GetUtcNow(),
                route.EncodedPolyline);
        }
        catch (RpcException erreur)
        {
            // AU NIVEAU « INFORMATION » ET NON « ERREUR ». Une durée absente est
            // un cas prévu et traité par l'appelant ; la journaliser comme une
            // panne ferait sonner une alerte à chaque livreur hors couverture.
            // AU NIVEAU « INFORMATION » ET NON « ERREUR ». Une mesure absente est
            // un cas prevu et traite par l'appelant ; la journaliser comme une
            // panne ferait sonner une alerte a chaque livreur hors couverture.
            //
            // AUCUNE COORDONNEE DANS LE JOURNAL. L'identifiant du livreur suffit a
            // retrouver l'appel ; y joindre sa position ferait du journal une
            // piste de deplacement, que le point 37 refuse par ailleurs.
            logger.LogInformation(
                erreur,
                "Itineraire indisponible pour le livreur {Livreur} : {Statut}.",
                driverId,
                erreur.StatusCode);

            return null;
        }
    }

    /// <summary>
    /// Le profil de circulation, depuis le véhicule du livreur.
    /// </summary>
    ///
    /// <remarks>
    /// IL NE RECOPIE PAS VehicleType, et c'est le contrat de Routing qui le veut :
    /// un tricycle et une voiture roulent sur les mêmes voies, donc ils partagent
    /// un profil. Ce qui compte est la FAÇON DE CIRCULER, pas le véhicule.
    /// </remarks>
    private static RouteProfile Profil(string vehicule) => vehicule?.ToLowerInvariant() switch
    {
        "motorcycle" or "moto" => RouteProfile.Motorcycle,
        "bicycle" or "velo" => RouteProfile.Bicycle,
        _ => RouteProfile.Car,
    };
}
