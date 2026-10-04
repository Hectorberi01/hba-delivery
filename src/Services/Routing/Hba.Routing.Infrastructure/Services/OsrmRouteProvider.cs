using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Hba.BuildingBlocks.Application.Abstractions;
using Hba.Routing.Application.Common;
using Hba.Routing.Domain.Routes;
using Hba.Routing.Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace Hba.Routing.Infrastructure.Services;

/// <summary>
/// Itinéraires calculés par OSRM.
/// </summary>
///
/// <remarks>
/// OSRM EST INTERROGÉ EN HTTP, PAS PAR UN SDK, et c'est volontaire : son API
/// tient en deux routes, et aucune bibliothèque ne vaut la peine d'être
/// introduite pour cela. C'est aussi ce qui rend ce fichier remplaçable par un
/// GoogleRoutesProvider sans que rien d'autre ne bouge.
///
/// LES COORDONNÉES S'ÉCRIVENT LONGITUDE D'ABORD. C'est l'inverse de tout le
/// reste du dépôt, où un point se lit « latitude, longitude ». OSRM suit la
/// convention GeoJSON, et inverser les deux ne produit PAS une erreur : cela
/// produit un itinéraire parfaitement valide, quelque part en Somalie. C'est le
/// genre de faute qui passe les tests et se voit sur la facture.
///
/// CE PROVIDER NE REPLIE PAS LUI-MÊME. Il lève, et c'est le handler qui décide :
/// un adaptateur qui masque ses pannes empêche l'application de savoir que la
/// mesure est dégradée.
/// </remarks>
internal sealed class OsrmRouteProvider(
    HttpClient http,
    IOptions<RoutingOptions> options,
    IClock clock) : IRouteProvider
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public string Name => "osrm";

    public async Task<Route> CalculateAsync(
        GeoPoint origin,
        GeoPoint destination,
        RouteProfile profile,
        bool includePolyline,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(origin);
        ArgumentNullException.ThrowIfNull(destination);

        var url = $"{Base(profile)}/route/v1/{Profil(profile)}/"
            + $"{Point(origin)};{Point(destination)}"
            + $"?overview={(includePolyline ? "full" : "false")}&geometries=polyline";

        var reponse = await LireAsync<OsrmRoute>(url, cancellationToken).ConfigureAwait(false);

        if (reponse.Code != "Ok" || reponse.Routes.Count == 0)
        {
            throw new InvalidOperationException(
                $"OSRM a repondu « {reponse.Code} » sans itineraire.");
        }

        var trajet = reponse.Routes[0];

        return Route.Create(
            (int)Math.Round(trajet.Distance),
            (int)Math.Round(trajet.Duration),
            Name,
            clock.UtcNow,
            includePolyline ? trajet.Geometry : null);
    }

    public async Task<RouteMatrix> CalculateMatrixAsync(
        IReadOnlyList<GeoPoint> origins,
        IReadOnlyList<GeoPoint> destinations,
        RouteProfile profile,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(origins);
        ArgumentNullException.ThrowIfNull(destinations);

        // UNE SEULE LISTE DE POINTS, PUIS DEUX JEUX D'INDEX. C'est la forme
        // qu'OSRM impose : les origines sont les premiers points, les
        // destinations les suivants, et « sources » / « destinations » designent
        // leurs positions dans cette liste unique.
        var points = origins.Concat(destinations).Select(Point);
        var sources = string.Join(';', Enumerable.Range(0, origins.Count));
        var cibles = string.Join(';', Enumerable.Range(origins.Count, destinations.Count));

        var url = $"{Base(profile)}/table/v1/{Profil(profile)}/{string.Join(';', points)}"
            + $"?sources={sources}&destinations={cibles}&annotations=distance,duration";

        var reponse = await LireAsync<OsrmTable>(url, cancellationToken).ConfigureAwait(false);

        if (reponse.Code != "Ok" || reponse.Durations.Count == 0)
        {
            throw new InvalidOperationException(
                $"OSRM a repondu « {reponse.Code} » sans matrice.");
        }

        var lignes = new List<IReadOnlyList<MatrixCell>>(origins.Count);

        for (var i = 0; i < reponse.Durations.Count; i++)
        {
            var cases = new List<MatrixCell>(destinations.Count);

            for (var j = 0; j < reponse.Durations[i].Count; j++)
            {
                var duree = reponse.Durations[i][j];
                var distance = reponse.Distances.Count > i && reponse.Distances[i].Count > j
                    ? reponse.Distances[i][j]
                    : null;

                // UN NUL VEUT DIRE « AUCUN CHEMIN », PAS « ZERO ». Le traduire
                // en zero ferait du point injoignable le meilleur candidat de
                // la vague.
                cases.Add(duree is null || distance is null
                    ? MatrixCell.Unreachable
                    : MatrixCell.Reached((int)Math.Round(distance.Value), (int)Math.Round(duree.Value)));
            }

            lignes.Add(cases);
        }

        return RouteMatrix.Create(lignes, Name, clock.UtcNow);
    }

    /// <summary>
    /// Lit une reponse d'OSRM, ou leve.
    ///
    /// AUCUNE TOLERANCE ICI, ET C'EST LE ROLE DE CET ADAPTATEUR : un corps vide,
    /// un statut d'erreur ou un JSON illisible sont des pannes du fournisseur.
    /// Les avaler rendrait une mesure inventee ; les lever laisse le handler
    /// replier en connaissance de cause.
    /// </summary>
    private async Task<T> LireAsync<T>(string url, CancellationToken cancellationToken)
    {
        using var reponse = await http.GetAsync(new Uri(url, UriKind.Absolute), cancellationToken)
            .ConfigureAwait(false);

        reponse.EnsureSuccessStatusCode();

        var contenu = await reponse.Content
            .ReadFromJsonAsync<T>(Json, cancellationToken)
            .ConfigureAwait(false);

        return contenu ?? throw new InvalidOperationException("OSRM a repondu un corps vide.");
    }

    /// <summary>Longitude d'abord : convention GeoJSON, suivie par OSRM.</summary>
    private static string Point(GeoPoint p) =>
        p.Longitude.ToString("0.######", CultureInfo.InvariantCulture)
        + "," + p.Latitude.ToString("0.######", CultureInfo.InvariantCulture);

    /// <summary>
    /// Le segment de profil de l'URL.
    ///
    /// IL EST LARGEMENT DECORATIF, et le savoir evite une longue enquete : le
    /// profil d'OSRM est fige au PRE-TRAITEMENT de la carte. Une instance
    /// preparee en voiture repond en voiture, meme interrogee en « bike ».
    /// Servir reellement trois profils demande trois instances — voir
    /// RoutingOptions.OsrmEndpoints.
    /// </summary>
    private static string Profil(RouteProfile profile) => profile switch
    {
        RouteProfile.Bicycle => "bike",
        _ => "driving",
    };

    private string Base(RouteProfile profile)
    {
        var cle = profile.ToString().ToLowerInvariant();

        return options.Value.OsrmEndpoints.TryGetValue(cle, out var adresse)
               && !string.IsNullOrWhiteSpace(adresse)
            ? adresse.TrimEnd('/')
            : options.Value.OsrmBaseUrl.TrimEnd('/');
    }

    private sealed record OsrmRoute(
        [property: JsonPropertyName("code")] string Code,
        [property: JsonPropertyName("routes")] IReadOnlyList<OsrmLeg> Routes);

    private sealed record OsrmLeg(
        [property: JsonPropertyName("distance")] double Distance,
        [property: JsonPropertyName("duration")] double Duration,
        [property: JsonPropertyName("geometry")] string Geometry);

    private sealed record OsrmTable(
        [property: JsonPropertyName("code")] string Code,
        [property: JsonPropertyName("durations")] IReadOnlyList<IReadOnlyList<double?>> Durations,
        [property: JsonPropertyName("distances")] IReadOnlyList<IReadOnlyList<double?>> Distances);
}
