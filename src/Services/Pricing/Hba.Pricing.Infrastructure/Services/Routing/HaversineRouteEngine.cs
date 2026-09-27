using Hba.Pricing.Application.Common.Interfaces;
using Hba.Pricing.Domain.Quotes;
using Hba.Pricing.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace Hba.Pricing.Infrastructure.Services.Routing;

/// <summary>
/// Distance a vol d'oiseau entre deux points, duree nulle.
///
/// CET ADAPTATEUR CONTREDIT DEUX ENDROITS DU PROJET, ET C'EST ASSUME.
/// Le contrat protobuf annonce « Distance et duree renvoyees par OSRM », et
/// RouteMeasurement previent qu'« une distance a vol d'oiseau sous-facturerait
/// toutes les courses qui contournent la lagune ». Les deux ont raison : a
/// Cotonou, un trajet qui longe la lagune peut faire le double du segment
/// droit, et ce manque a gagner est paye par HBA a chaque course.
///
/// Il est pose ici sur demande explicite du produit, pour afficher un prix
/// pendant que le moteur de routage reste a choisir. C'est une DETTE, pas une
/// solution : elle se solde en remplacant cette classe par un client OSRM,
/// sans qu'une ligne du domaine ni du handler ne bouge. La duree renvoyee est
/// nulle, ce qui est coherent tant que le prix a la minute vaut zero — mais
/// deviendra faux des que ce tarif sera non nul.
/// </summary>
internal sealed class HaversineRouteEngine(ILogger<HaversineRouteEngine> logger) : IRouteEngine
{
    private const double EarthRadiusMeters = 6_371_000d;

    public Task<RouteMeasurement> MeasureAsync(
        GeoPoint pickup,
        GeoPoint dropoff,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(pickup);
        ArgumentNullException.ThrowIfNull(dropoff);

        var distance = (int)Math.Round(DistanceMeters(pickup, dropoff));

        logger.LogDebug(
            "Trajet mesure a vol d'oiseau : {Distance} m. Aucun moteur de routage n'est branche.",
            distance);

        return Task.FromResult(RouteMeasurement.Create(distance, durationSeconds: 0));
    }

    private static double DistanceMeters(GeoPoint from, GeoPoint to)
    {
        var lat1 = ToRadians(from.Latitude);
        var lat2 = ToRadians(to.Latitude);
        var deltaLat = ToRadians(to.Latitude - from.Latitude);
        var deltaLon = ToRadians(to.Longitude - from.Longitude);

        var a = (Math.Sin(deltaLat / 2) * Math.Sin(deltaLat / 2))
                + (Math.Cos(lat1) * Math.Cos(lat2) * Math.Sin(deltaLon / 2) * Math.Sin(deltaLon / 2));

        return EarthRadiusMeters * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }

    private static double ToRadians(double degrees) => degrees * Math.PI / 180d;
}
