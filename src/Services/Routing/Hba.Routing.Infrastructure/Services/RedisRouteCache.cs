using System.Globalization;
using System.Text.Json;
using Hba.Routing.Application.Common;
using Hba.Routing.Domain.Routes;
using Hba.Routing.Infrastructure.Options;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Hba.Routing.Infrastructure.Services;

/// <summary>
/// Les itinéraires déjà calculés, dans Redis.
/// </summary>
///
/// <remarks>
/// LA CLÉ ARRONDIT LES COORDONNÉES, ET C'EST TOUT L'ART DE CE CACHE. Deux
/// demandes pour « la même adresse » n'ont jamais exactement les mêmes chiffres
/// — le GPS bouge de quelques mètres, le doigt sur la carte aussi. Sans
/// arrondi, le taux de succès serait proche de zéro et le cache ne servirait
/// qu'à occuper de la mémoire.
///
/// CINQ DÉCIMALES, SOIT ENVIRON UN MÈTRE. Assez fin pour ne jamais confondre
/// deux portails voisins, assez large pour que deux relevés du même point
/// tombent dans la même case.
///
/// LE TRACÉ EST DANS LA CLÉ, PAS SEULEMENT DANS LA VALEUR. Une mesure demandée
/// sans tracé ne peut pas servir à un appelant qui en veut un : ce sont deux
/// entrées différentes, sinon la carte du client resterait vide parce qu'un
/// devis était passé avant lui.
/// </remarks>
internal sealed class RedisRouteCache(
    IConnectionMultiplexer redis,
    IOptions<RoutingOptions> options) : IRouteCache
{
    private static readonly JsonSerializerOptions Json = new();

    public async Task<Route?> GetAsync(
        GeoPoint origin,
        GeoPoint destination,
        RouteProfile profile,
        bool withPolyline,
        CancellationToken cancellationToken)
    {
        if (options.Value.CacheTtlSeconds <= 0)
        {
            return null;
        }

        var brut = await redis.GetDatabase()
            .StringGetAsync(Cle(origin, destination, profile, withPolyline))
            .ConfigureAwait(false);

        if (brut.IsNullOrEmpty)
        {
            return null;
        }

        try
        {
            var stocke = JsonSerializer.Deserialize<RouteStockee>(brut!, Json);

            return stocke is null
                ? null
                : Route.Create(
                    stocke.DistanceMeters,
                    stocke.DurationSeconds,
                    stocke.Provider,
                    stocke.CalculatedAt,
                    stocke.EncodedPolyline);
        }
        catch (JsonException)
        {
            // UNE ENTREE ILLISIBLE N'EST PAS UNE PANNE : c'est un format qui a
            // change entre deux versions. On recalcule, et la prochaine ecriture
            // remplacera l'ancienne.
            return null;
        }
    }

    public Task SetAsync(
        GeoPoint origin,
        GeoPoint destination,
        RouteProfile profile,
        Route route,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(route);

        // ON NE MET JAMAIS EN CACHE UN REPLI. Une panne d'OSRM d'une minute
        // laisserait sinon des mesures a vol d'oiseau pendant une heure, et tous
        // les prix figes entre-temps seraient sous-estimes.
        if (route.FromFallback || options.Value.CacheTtlSeconds <= 0)
        {
            return Task.CompletedTask;
        }

        var valeur = JsonSerializer.Serialize(
            new RouteStockee(
                route.DistanceMeters,
                route.DurationSeconds,
                route.EncodedPolyline,
                route.Provider,
                route.CalculatedAt),
            Json);

        return redis.GetDatabase().StringSetAsync(
            Cle(origin, destination, profile, route.EncodedPolyline.Length > 0),
            valeur,
            TimeSpan.FromSeconds(options.Value.CacheTtlSeconds));
    }

    private static string Cle(GeoPoint origine, GeoPoint destination, RouteProfile profile, bool avecTrace)
        => string.Create(
            CultureInfo.InvariantCulture,
            $"route:{Arrondi(origine)}:{Arrondi(destination)}:{profile}:{(avecTrace ? "t" : "n")}");

    private static string Arrondi(GeoPoint p) => string.Create(
        CultureInfo.InvariantCulture,
        $"{p.Latitude:F5},{p.Longitude:F5}");

    private sealed record RouteStockee(
        int DistanceMeters,
        int DurationSeconds,
        string EncodedPolyline,
        string Provider,
        DateTimeOffset CalculatedAt);
}
