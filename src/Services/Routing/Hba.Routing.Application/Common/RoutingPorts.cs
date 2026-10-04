using Hba.Routing.Domain.Routes;

namespace Hba.Routing.Application.Common;

/// <summary>
/// Un fournisseur d'itinéraires.
/// </summary>
///
/// <remarks>
/// C'EST LA SEULE FRONTIÈRE AVEC LE MONDE EXTÉRIEUR, et tout l'intérêt du
/// service tient là : OSRM, Google ou Mapbox se remplacent en changeant
/// l'implémentation, sans qu'une ligne de Pricing, de Dispatch ou de Ride ne
/// bouge.
///
/// IL PEUT ÉCHOUER, ET C'EST NORMAL. Un moteur de routage est un service
/// distant ; il tombe, il expire, il répond une erreur. L'implémentation ne
/// masque pas ces cas : elle lève, et c'est l'application qui décide du repli.
/// </remarks>
public interface IRouteProvider
{
    /// <summary>Nom porté par la mesure : « osrm », « haversine ».</summary>
    string Name { get; }

    Task<Route> CalculateAsync(
        GeoPoint origin,
        GeoPoint destination,
        RouteProfile profile,
        bool includePolyline,
        CancellationToken cancellationToken);

    Task<RouteMatrix> CalculateMatrixAsync(
        IReadOnlyList<GeoPoint> origins,
        IReadOnlyList<GeoPoint> destinations,
        RouteProfile profile,
        CancellationToken cancellationToken);
}

/// <summary>
/// Le fournisseur de dernier recours.
/// </summary>
///
/// <remarks>
/// UN TYPE À PART PLUTÔT QU'UN ORDRE DANS UNE LISTE. Écrire
/// « le second de la collection » aurait fait dépendre une décision de secours
/// de l'ordre d'enregistrement dans le conteneur — un ordre que personne ne
/// relit et qu'un refactoring change sans le dire.
///
/// IL NE TOMBE JAMAIS, ET C'EST SA SEULE QUALITÉ. Le calcul est local, sans
/// réseau : il répond toujours, mais à vol d'oiseau. Toute mesure qui en vient
/// porte <c>FromFallback</c>.
/// </remarks>
public interface IFallbackRouteProvider : IRouteProvider;

/// <summary>
/// Le cache des itinéraires.
/// </summary>
///
/// <remarks>
/// LES TRAJETS SE RÉPÈTENT ÉNORMÉMENT. Un client demande un devis, hésite,
/// recommence ; le dispatch ouvre trois vagues depuis le même point
/// d'enlèvement. Sans cache, chacun de ces gestes redemande le même calcul.
///
/// ON NE MET JAMAIS EN CACHE UN REPLI. Une mesure à vol d'oiseau conservée une
/// heure ferait durer une panne d'OSRM bien après sa fin, et tous les prix
/// figés pendant ce temps seraient sous-estimés.
/// </remarks>
public interface IRouteCache
{
    Task<Route?> GetAsync(
        GeoPoint origin,
        GeoPoint destination,
        RouteProfile profile,
        bool withPolyline,
        CancellationToken cancellationToken);

    Task SetAsync(
        GeoPoint origin,
        GeoPoint destination,
        RouteProfile profile,
        Route route,
        CancellationToken cancellationToken);
}
