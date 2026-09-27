using Hba.BuildingBlocks.Grpc.ServiceAuth;
using Hba.Contracts.Driver.V1;
using Hba.Dispatch.Application.Common.Interfaces;
using Hba.Dispatch.Domain.Dispatching;
// DEUX « DriverAvailability » SE CROISENT ICI : le verdict du port, cote
// application, et le message du contrat. C'est exactement le role de cet
// adaptateur que de traduire l'un en l'autre, donc la collision est normale —
// elle se leve par un alias, pas en renommant l'un des deux.
using DomainAvailability = Hba.Dispatch.Application.Common.Interfaces.DriverAvailability;
using DomainGeoPoint = Hba.Dispatch.Domain.ValueObjects.GeoPoint;

namespace Hba.Dispatch.Infrastructure.Services.Drivers;

/// <summary>
/// Adaptateur vers le service Driver.
///
/// DISPATCH NE SAIT NI OU SONT LES LIVREURS, NI QUI EST LIBRE. Il pose la
/// question et prend la reponse. C'est ce qui permet a Driver de changer sa
/// facon de tenir les positions — Redis GEO aujourd'hui, autre chose demain —
/// sans que le moteur de vagues s'en apercoive.
/// </summary>
/// <remarks>
/// AUCUN DE CES APPELS NE REPORTE LE JETON DE L'APPELANT, et les deux n'ont
/// pourtant pas la meme origine : la recherche de proximite part du
/// planificateur, ou il n'y a aucun utilisateur ; la verification d'un livreur
/// part d'une requete de l'exploitation, dont l'autorisation a deja ete
/// verifiee dans le handler. Dans les deux cas Driver est en [Authorize] et
/// c'est le jeton de service qui ouvre la porte. Voir ADR 0018.
/// </remarks>
internal sealed class DriverFinder(
    DriverService.DriverServiceClient client,
    IServiceTokenProvider tokens) : IDriverFinder
{
    public async Task<IReadOnlyList<CandidateDriver>> FindAvailableNearbyAsync(
        DomainGeoPoint center,
        int radiusMeters,
        int limit,
        IReadOnlyCollection<string> excludeDriverIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(center);
        ArgumentNullException.ThrowIfNull(excludeDriverIds);

        var request = new FindAvailableNearbyRequest
        {
            Center = new Hba.Contracts.Common.V1.GeoPoint
            {
                Latitude = center.Latitude,
                Longitude = center.Longitude,
            },
            RadiusMeters = radiusMeters,
            Limit = limit,

            // LE VEHICULE N'EST PAS FILTRE, ET C'EST UN MANQUE ASSUME.
            // « DeliveryConfirmed » ne transporte pas le poids du colis, et le
            // devis a ete calcule sur une moto par defaut. Filtrer sur un type
            // que nous ne connaissons pas reviendrait a inventer une regle.
            // Le jour ou le contrat portera le poids, une ligne suffira.
            VehicleType = Hba.Contracts.Common.V1.VehicleType.Unspecified,
        };

        request.ExcludeDriverIds.AddRange(excludeDriverIds);

        var headers = await tokens.AuthorizationAsync(cancellationToken).ConfigureAwait(false);

        var response = await client
            .FindAvailableNearbyAsync(request, headers: headers, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return [.. response.Drivers.Select(d => new CandidateDriver(d.DriverId, d.DistanceMeters))];
    }

    public async Task<DomainAvailability> CheckAsync(
        string driverId,
        DomainGeoPoint reference,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reference);

        // LE JETON DE SERVICE ICI AUSSI, ALORS QUE CET APPEL-LA PART D'UNE
        // REQUETE ADMIN. Dispatch n'est pas une passerelle : il ne reporte pas
        // le jeton de l'operateur vers Driver. L'autorisation de l'operateur a
        // ete verifiee dans le handler, ou elle appartient. Voir ADR 0018.
        var headers = await tokens.AuthorizationAsync(cancellationToken).ConfigureAwait(false);

        var response = await client
            .CheckDriverAvailabilityAsync(
                new CheckDriverAvailabilityRequest
                {
                    DriverId = driverId,
                    Reference = new Hba.Contracts.Common.V1.GeoPoint
                    {
                        Latitude = reference.Latitude,
                        Longitude = reference.Longitude,
                    },
                },
                headers: headers,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return new DomainAvailability(
            response.Offerable,
            string.IsNullOrEmpty(response.Reason) ? null : response.Reason,
            response.DistanceMeters);
    }
}
