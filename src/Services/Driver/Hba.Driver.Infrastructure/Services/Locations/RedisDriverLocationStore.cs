using System.Globalization;
using Hba.Driver.Application.Common.Interfaces;
using Hba.Driver.Domain.ValueObjects;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Hba.Driver.Infrastructure.Services.Locations;

/// <summary>
/// Positions des livreurs dans Redis GEO.
///
/// DEUX CLES, ET LA SECONDE N'EST PAS UN LUXE. La premiere est l'index
/// geographique ; la seconde retient QUAND chaque position est arrivee. Redis
/// GEO ne sait pas faire expirer un membre : sans horodatage a cote, la
/// derniere position d'un telephone eteint resterait indefiniment dans
/// l'index, et le dispatch proposerait des courses a quelqu'un rentre chez lui
/// depuis une heure.
/// </summary>
internal sealed class RedisDriverLocationStore(
    IConnectionMultiplexer redis,
    IOptions<DriverLocationOptions> options) : IDriverLocationStore
{
    private const string PositionsKey = "driver:positions";
    private const string SeenKey = "driver:positions:seen";

    /// <summary>
    /// LE RAYON TERRESTRE DE REDIS, PAS CELUI DES MANUELS.
    ///
    /// GEORADIUS mesure sur une sphere de 6 372 797,560856 m ; prendre les
    /// 6 371 000 m habituels ici rendrait deux distances differentes pour le
    /// meme couple de points selon qu'on passe par la recherche de proximite
    /// ou par cette lecture. L'ecart est de trois pour mille — invisible sur
    /// le terrain, et largement suffisant pour faire douter de la mesure
    /// quand deux ecrans l'affichent cote a cote.
    /// </summary>
    private const double EarthRadiusMeters = 6372797.560856;

    private const double EnRadians = Math.PI / 180d;

    private readonly DriverLocationOptions _options = options.Value;

    public int FreshnessSeconds => _options.FreshnessSeconds;

    public async Task UpsertAsync(
        Guid driverId,
        GeoPoint position,
        DateTimeOffset capturedAt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(position);

        var member = Member(driverId);
        var db = redis.GetDatabase();

        // UN LOT, PAS DEUX ALLERS-RETOURS. C'est l'ecriture la plus frequente
        // du systeme ; la faire en deux commandes doublerait la latence reseau
        // pour chaque battement de chaque livreur en ligne.
        var batch = db.CreateBatch();

        var geo = batch.GeoAddAsync(PositionsKey, position.Longitude, position.Latitude, member);
        var seen = batch.SortedSetAddAsync(SeenKey, member, capturedAt.ToUnixTimeSeconds());

        batch.Execute();

        await Task.WhenAll(geo, seen).ConfigureAwait(false);
    }

    public async Task RemoveAsync(Guid driverId, CancellationToken cancellationToken)
    {
        var member = Member(driverId);
        var db = redis.GetDatabase();

        var batch = db.CreateBatch();

        // GeoRemove et SortedSetRemove visent le meme type de structure : un
        // index GEO EST un ensemble trie. Les deux cles se nettoient donc de
        // la meme facon.
        var geo = batch.GeoRemoveAsync(PositionsKey, member);
        var seen = batch.SortedSetRemoveAsync(SeenKey, member);

        batch.Execute();

        await Task.WhenAll(geo, seen).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<LocatedDriver>> SearchAsync(
        GeoPoint center,
        int radiusMeters,
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(center);

        var db = redis.GetDatabase();

        var results = await db.GeoRadiusAsync(
            PositionsKey,
            center.Longitude,
            center.Latitude,
            radiusMeters,
            GeoUnit.Meters,
            limit,
            Order.Ascending,
            GeoRadiusOptions.WithCoordinates | GeoRadiusOptions.WithDistance).ConfigureAwait(false);

        if (results.Length == 0)
        {
            return [];
        }

        var membres = results.Select(r => r.Member).ToArray();

        // UNE SEULE COMMANDE POUR TOUS LES HORODATAGES. Interroger le score
        // livreur par livreur ferait un aller-retour par candidat, sur le
        // chemin critique d'une vague.
        var scores = await db.SortedSetScoresAsync(SeenKey, membres).ConfigureAwait(false);

        var limite = DateTimeOffset.UtcNow.AddSeconds(-_options.FreshnessSeconds).ToUnixTimeSeconds();

        var frais = new List<LocatedDriver>(results.Length);

        for (var i = 0; i < results.Length; i++)
        {
            var score = scores[i];
            if (score is null || score.Value < limite)
            {
                continue;
            }

            var entry = results[i];
            if (entry.Position is not { } coordonnees || entry.Distance is not { } distance)
            {
                continue;
            }

            if (!Guid.TryParse(entry.Member.ToString(), out var driverId))
            {
                continue;
            }

            frais.Add(new LocatedDriver(
                driverId,
                (int)Math.Round(distance, MidpointRounding.AwayFromZero),
                coordonnees.Latitude,
                coordonnees.Longitude));
        }

        return frais;
    }

    public async Task<IReadOnlyList<DriverPosition>> ListFreshAsync(
        int limit,
        CancellationToken cancellationToken)
    {
        var db = redis.GetDatabase();

        var limite = DateTimeOffset.UtcNow.AddSeconds(-_options.FreshnessSeconds).ToUnixTimeSeconds();
        var plafond = Math.Clamp(limit, 1, 1000);

        // ON PART DE L'ENSEMBLE DES HORODATAGES, PAS DE L'INDEX GEO. L'index
        // n'a pas de notion de fraicheur : il rendrait aussi les positions
        // mortes. Ici le tri par score fait le travail, et « Descending »
        // garde les plus recentes quand le plafond mord.
        var recents = await db.SortedSetRangeByScoreWithScoresAsync(
            SeenKey,
            limite,
            double.PositiveInfinity,
            Exclude.None,
            Order.Descending,
            0,
            plafond).ConfigureAwait(false);

        if (recents.Length == 0)
        {
            return [];
        }

        var membres = recents.Select(e => e.Element).ToArray();

        // UNE SEULE COMMANDE POUR TOUTES LES COORDONNEES.
        var coordonnees = await db.GeoPositionAsync(PositionsKey, membres).ConfigureAwait(false);

        var positions = new List<DriverPosition>(recents.Length);

        for (var i = 0; i < recents.Length; i++)
        {
            // UN HORODATAGE SANS COORDONNEE N'EST PAS UNE ERREUR : les deux
            // cles se nettoient dans le meme lot, mais rien ne garantit
            // qu'un lecteur ne tombe pas entre les deux ecritures. On ignore,
            // on ne leve pas — une carte a qui il manque un point reste utile.
            if (coordonnees[i] is not { } point)
            {
                continue;
            }

            if (!Guid.TryParse(recents[i].Element.ToString(), out var driverId))
            {
                continue;
            }

            positions.Add(new DriverPosition(
                driverId,
                point.Latitude,
                point.Longitude,
                DateTimeOffset.FromUnixTimeSeconds((long)recents[i].Score)));
        }

        return positions;
    }

    public async Task<MeasuredPosition?> MeasureAsync(
        Guid driverId,
        GeoPoint reference,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reference);

        var member = Member(driverId);
        var db = redis.GetDatabase();

        // LES DEUX CLES DANS UN LOT : la position et son horodatage se lisent
        // ensemble ou la reponse n'a pas de sens.
        var batch = db.CreateBatch();

        var pointTask = batch.GeoPositionAsync(PositionsKey, member);
        var scoreTask = batch.SortedSetScoreAsync(SeenKey, member);

        batch.Execute();

        var point = await pointTask.ConfigureAwait(false);
        var score = await scoreTask.ConfigureAwait(false);

        if (point is not { } coordonnees || score is null)
        {
            return null;
        }

        var limite = DateTimeOffset.UtcNow.AddSeconds(-_options.FreshnessSeconds).ToUnixTimeSeconds();

        // UNE POSITION PERIMEE VAUT PAS DE POSITION, et la nuance ne se rend
        // pas au lecteur : « il etait la il y a une heure » n'aide personne a
        // decider s'il peut prendre la course maintenant.
        if (score.Value < limite)
        {
            return null;
        }

        var distance = DistanceMeters(
            reference.Latitude,
            reference.Longitude,
            coordonnees.Latitude,
            coordonnees.Longitude);

        return new MeasuredPosition(
            driverId,
            coordonnees.Latitude,
            coordonnees.Longitude,
            (int)Math.Round(distance, MidpointRounding.AwayFromZero),
            DateTimeOffset.FromUnixTimeSeconds((long)score.Value));
    }

    /// <summary>Distance orthodromique entre deux points, en metres.</summary>
    private static double DistanceMeters(double lat1, double lon1, double lat2, double lon2)
    {
        var phi1 = lat1 * EnRadians;
        var phi2 = lat2 * EnRadians;
        var dPhi = (lat2 - lat1) * EnRadians;
        var dLambda = (lon2 - lon1) * EnRadians;

        var sinPhi = Math.Sin(dPhi / 2d);
        var sinLambda = Math.Sin(dLambda / 2d);

        var a = (sinPhi * sinPhi) + (Math.Cos(phi1) * Math.Cos(phi2) * sinLambda * sinLambda);

        return 2d * EarthRadiusMeters * Math.Asin(Math.Min(1d, Math.Sqrt(a)));
    }

    private static RedisValue Member(Guid driverId) => driverId.ToString("D", CultureInfo.InvariantCulture);
}
