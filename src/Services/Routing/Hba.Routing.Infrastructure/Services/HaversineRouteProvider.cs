using Hba.BuildingBlocks.Application.Abstractions;
using Hba.Routing.Application.Common;
using Hba.Routing.Domain.Routes;

namespace Hba.Routing.Infrastructure.Services;

/// <summary>
/// Mesure à vol d'oiseau. Le fournisseur de dernier recours.
/// </summary>
///
/// <remarks>
/// CE CODE VIENT DE PRICING, ET IL Y ÉTAIT LE SEUL MOTEUR. Jusqu'au 1er octobre
/// 2026, tous les prix de la plateforme étaient calculés ainsi — le commentaire
/// de <c>HaversineRouteEngine</c> annonçait d'ailleurs qu'OSRM le remplacerait.
/// Il n'est pas supprimé mais RÉTROGRADÉ : de moteur unique, il devient le filet.
///
/// CE QU'IL SOUS-ESTIME, ET DE COMBIEN. Une ligne droite ignore les rues, les
/// sens uniques et les ponts. En ville l'écart est d'environ un tiers ; il
/// double quand le trajet contourne la lagune de Cotonou. C'est précisément
/// pourquoi toute mesure qui en vient porte <c>FromFallback</c>.
///
/// LA DURÉE EST UNE CONVENTION, PAS UNE MESURE. Il n'existe aucun moyen de
/// déduire un temps de trajet d'une distance à vol d'oiseau : on applique une
/// vitesse moyenne par profil, et on l'assume comme telle.
/// </remarks>
internal sealed class HaversineRouteProvider(IClock clock) : IFallbackRouteProvider
{
    private const double RayonTerrestreMetres = 6_371_000d;

    /// <summary>
    /// Vitesses moyennes retenues, en mètres par seconde.
    ///
    /// ELLES DÉCRIVENT COTONOU AUX HEURES OUVRÉES, pas une route dégagée : une
    /// moto y avance à environ vingt-cinq kilomètres à l'heure, une voiture
    /// moins vite qu'elle dans les embouteillages, un vélo à quinze.
    /// </summary>
    private static double VitesseMoyenne(RouteProfile profile) => profile switch
    {
        RouteProfile.Bicycle => 15_000d / 3_600d,
        RouteProfile.Car => 22_000d / 3_600d,
        _ => 25_000d / 3_600d,
    };

    public string Name => "haversine";

    public Task<Route> CalculateAsync(
        GeoPoint origin,
        GeoPoint destination,
        RouteProfile profile,
        bool includePolyline,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(origin);
        ArgumentNullException.ThrowIfNull(destination);

        var (distance, duree) = Mesure(origin, destination, profile);

        // AUCUN TRACÉ, MÊME DEMANDÉ. Dessiner la ligne droite sur la carte du
        // client lui montrerait un itinéraire qui traverse des maisons.
        return Task.FromResult(Route.Create(
            distance,
            duree,
            Name,
            clock.UtcNow,
            encodedPolyline: null,
            fromFallback: true));
    }

    public Task<RouteMatrix> CalculateMatrixAsync(
        IReadOnlyList<GeoPoint> origins,
        IReadOnlyList<GeoPoint> destinations,
        RouteProfile profile,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(origins);
        ArgumentNullException.ThrowIfNull(destinations);

        var lignes = origins
            .Select(origine => (IReadOnlyList<MatrixCell>)destinations
                .Select(destination =>
                {
                    var (distance, duree) = Mesure(origine, destination, profile);
                    return MatrixCell.Reached(distance, duree);
                })
                .ToList())
            .ToList();

        // TOUT EST JOIGNABLE A VOL D'OISEAU, et c'est exactement ce qui rend
        // cette matrice dangereuse pour un classement : elle ne sait pas qu'un
        // point est de l'autre cote de la lagune. Le drapeau previent.
        return Task.FromResult(RouteMatrix.Create(lignes, Name, clock.UtcNow, fromFallback: true));
    }

    private static (int Distance, int Duree) Mesure(
        GeoPoint origine,
        GeoPoint destination,
        RouteProfile profile)
    {
        var distance = DistanceMetres(origine, destination);
        var duree = distance / VitesseMoyenne(profile);

        return ((int)Math.Round(distance), (int)Math.Round(duree));
    }

    private static double DistanceMetres(GeoPoint a, GeoPoint b)
    {
        var lat1 = double.DegreesToRadians(a.Latitude);
        var lat2 = double.DegreesToRadians(b.Latitude);
        var dLat = lat2 - lat1;
        var dLon = double.DegreesToRadians(b.Longitude - a.Longitude);

        var h = (Math.Sin(dLat / 2) * Math.Sin(dLat / 2))
                + (Math.Cos(lat1) * Math.Cos(lat2) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2));

        return 2 * RayonTerrestreMetres * Math.Asin(Math.Min(1, Math.Sqrt(h)));
    }
}
