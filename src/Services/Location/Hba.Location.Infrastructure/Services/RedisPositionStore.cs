using System.Globalization;
using System.Text.Json;
using Hba.Location.Application.Common;
using Hba.Location.Domain.Positions;
using Hba.Location.Infrastructure.Options;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Hba.Location.Infrastructure.Services;

/// <summary>
/// Positions des chauffeurs dans Redis.
/// </summary>
///
/// <remarks>
/// TROIS CLES, ET AUCUNE N'EST UN LUXE.
///
/// — <c>driver:positions</c> est l'index GEOGRAPHIQUE. C'est lui qui sait repondre
///   « qui est pres d'ici », et lui seul : aucune autre structure de Redis ne fait
///   une recherche par rayon.
///
/// — <c>driver:positions:seen</c> retient QUAND chaque position est arrivee. Redis
///   GEO ne sait pas faire expirer un membre : sans horodatage a cote, la derniere
///   position d'un telephone eteint resterait indefiniment dans l'index, et le
///   dispatch proposerait des courses a quelqu'un rentre chez lui depuis une heure.
///
/// — <c>driver:positions:motion</c> porte le CAP, la VITESSE et la PRECISION. Un
///   index GEO ne stocke que deux coordonnees — c'est une contrainte de la structure,
///   pas un choix — donc tout ce qui accompagne le point doit vivre ailleurs. Cette
///   cle est arrivee avec le service, le 1er octobre 2026 : les deux premiers champs
///   existaient au contrat de Driver depuis le debut et etaient JETES a la frontiere
///   gRPC, si bien qu'aucune carte n'a jamais pu orienter une fleche.
///
/// LE TRIPLET S'ECRIT EN UN LOT ET SE LIT EN TROIS COMMANDES, jamais une par
/// chauffeur. C'est l'ecriture la plus frequente du systeme et la lecture est sur le
/// chemin critique d'une vague : un aller-retour par candidat multiplierait la latence
/// par le nombre de candidats.
///
/// RIEN N'EXPIRE TOUT SEUL, et c'est assume. Un chauffeur qui ne revient jamais laisse
/// trois entrees derriere lui ; la fenetre de fraicheur les rend invisibles, mais elles
/// occupent de la memoire. Poser un TTL demanderait de le reecrire a chaque battement
/// — une commande de plus sur l'appel le plus chaud — pour quelques octets par
/// chauffeur ayant quitte la plateforme. Le jour ou cela pesera, un balayage
/// periodique du membre le plus vieux de l'ensemble trie coutera moins.
/// </remarks>
internal sealed class RedisPositionStore(
    IConnectionMultiplexer redis,
    IOptions<LocationOptions> options) : IPositionStore
{
    private const string PositionsKey = "driver:positions";
    private const string SeenKey = "driver:positions:seen";
    private const string MotionKey = "driver:positions:motion";

    /// <summary>
    /// Separateur des trois nombres du mouvement, dans la valeur du hachage.
    ///
    /// UN FORMAT COMPACT PLUTOT QUE DU JSON, et la raison est la frequence : trois
    /// nombres par battement, par chauffeur en ligne. Un objet JSON porterait ses
    /// accolades et ses noms de champs a chaque ecriture, pour une valeur qui ne sort
    /// jamais de ce fichier — elle est ecrite ici et relue ici.
    ///
    /// UN SEGMENT VIDE VEUT DIRE « NON RENSEIGNE », ce qui n'est pas zero : un
    /// telephone a l'arret ne rend aucun cap, alors que zero degre veut dire « plein
    /// nord ». Le format doit donc distinguer les deux, et c'est pour cela qu'on
    /// n'ecrit pas « 0 » par defaut.
    /// </summary>
    private const char Separateur = ';';

    /// <summary>
    /// Nombre de champs du format. UNE VALEUR QUI N'EN A PAS AUTANT EST IGNOREE :
    /// elle vient d'une version anterieure du format ou d'une ecriture tronquee, et
    /// l'interpreter a moitie dessinerait une fleche fausse.
    /// </summary>
    private const int ChampsDuMouvement = 4;

    /// <summary>
    /// Format du canal : les noms des proprietes sont ceux de BattementPublie, qui les
    /// fixe par attribut. « Web » met les noms en minuscule initiale par defaut — sans
    /// effet ici, puisque chaque champ est nomme explicitement, mais c'est le reglage
    /// coherent avec le reste du depot.
    /// </summary>
    private static readonly JsonSerializerOptions FormatDuCanal = new(JsonSerializerDefaults.Web);

    private readonly LocationOptions _options = options.Value;

    public int FreshnessSeconds => _options.FreshnessSeconds;

    public async Task UpsertAsync(DriverPosition position, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(position);

        var membre = Membre(position.DriverId);
        var db = redis.GetDatabase();

        // UN LOT, PAS TROIS ALLERS-RETOURS. C'est l'ecriture la plus frequente du
        // systeme ; la faire en trois commandes triplerait la latence reseau pour
        // chaque battement de chaque chauffeur en ligne.
        var lot = db.CreateBatch();

        var geo = lot.GeoAddAsync(
            PositionsKey,
            position.Point.Longitude,
            position.Point.Latitude,
            membre);

        var vu = lot.SortedSetAddAsync(SeenKey, membre, position.SeenAt.ToUnixTimeSeconds());

        // LE MOUVEMENT INCONNU EST EFFACE, PAS ECRIT VIDE. Sans cela, un telephone qui
        // cesse de donner son cap laisserait le dernier cap connu a cote d'une
        // position neuve : la carte montrerait une fleche qui a l'air juste et qui a
        // une minute de retard.
        var mouvement = position.Motion.EstInconnu
            ? lot.HashDeleteAsync(MotionKey, membre)
            : lot.HashSetAsync(MotionKey, membre, Encoder(position.Motion, position.CapturedAt));

        lot.Execute();

        await Task.WhenAll(geo, vu, mouvement).ConfigureAwait(false);

        await PublierAsync(position).ConfigureAwait(false);
    }

    /// <summary>
    /// Annonce le battement sur le canal Redis, pour le temps reel.
    /// </summary>
    ///
    /// <remarks>
    /// APRES L'ECRITURE, ET SANS LA BLOQUER. L'index est la source de verite : un
    /// chauffeur dont la position est ecrite mais non annoncee reste trouvable par le
    /// dispatch et par la carte en sondage. L'inverse — annoncer puis echouer a ecrire —
    /// aurait fait bouger une pastille sur un ecran sans que personne d'autre ne puisse
    /// le confirmer.
    ///
    /// UN ECHEC DE PUBLICATION NE FAIT PAS ECHOUER LE BATTEMENT, et c'est la decision
    /// importante de cette methode. Le temps reel est un confort ; l'index est le metier.
    /// Faire remonter l'erreur ferait repondre 500 a l'application chauffeur pour un
    /// Redis pub/sub indisponible — donc un battement perdu, donc un chauffeur qui se
    /// rapproche de l'instant ou il sort des recherches. On consigne et on continue.
    ///
    /// PUBLISH NE GARANTIT RIEN, ET C'EST CE QU'ON VEUT. Redis ne met pas le message en
    /// file : s'il n'y a aucun abonne, il est perdu. Un battement perdu est remplace
    /// vingt secondes plus tard, et aucun ne merite d'etre conserve — c'est exactement
    /// pourquoi ce flux ne passe pas par Kafka.
    /// </remarks>
    private async Task PublierAsync(DriverPosition position)
    {
        if (string.IsNullOrWhiteSpace(_options.PositionChannel))
        {
            return;
        }

        try
        {
            var charge = JsonSerializer.Serialize(
                new BattementPublie
                {
                    DriverId = position.DriverId.ToString("D", CultureInfo.InvariantCulture),
                    Latitude = position.Point.Latitude,
                    Longitude = position.Point.Longitude,
                    CapDegres = position.Motion.HeadingDegrees,
                    VitesseMps = position.Motion.SpeedMps,
                    PrecisionMetres = position.Motion.AccuracyMeters,
                    VuAUnix = position.SeenAt.ToUnixTimeSeconds(),
                },
                FormatDuCanal);

            await redis.GetSubscriber()
                .PublishAsync(RedisChannel.Literal(_options.PositionChannel), charge)
                .ConfigureAwait(false);
        }
#pragma warning disable CA1031 // Le temps reel ne doit jamais faire echouer un battement.
        catch (Exception)
#pragma warning restore CA1031
        {
            // VOLONTAIREMENT MUET, ET C'EST LE SEUL ENDROIT DU DEPOT QUI SE LE PERMET.
            //
            // Ce chemin est le plus frequent du systeme : un point toutes les vingt
            // secondes par chauffeur en ligne. Un Redis pub/sub en panne produirait donc
            // des milliers de lignes identiques par minute, et noierait les journaux au
            // moment precis ou l'exploitation en a besoin pour diagnostiquer autre chose.
            //
            // CE N'EST PAS UNE PANNE INVISIBLE POUR AUTANT : si la publication echoue,
            // c'est que Redis est injoignable — or l'ECRITURE de l'index passe par le meme
            // Redis et aurait leve avant, bien plus bruyamment. Le seul cas ou ce catch
            // mord vraiment est un pub/sub qui refuse alors que les commandes passent, ce
            // qui ne s'observe pas.
        }
    }

    public async Task ForgetAsync(Guid driverId, CancellationToken cancellationToken)
    {
        var membre = Membre(driverId);
        var db = redis.GetDatabase();

        var lot = db.CreateBatch();

        // GeoRemove et SortedSetRemove visent le meme type de structure : un index GEO
        // EST un ensemble trie. Les deux premieres cles se nettoient donc de la meme
        // facon ; la troisieme est un hachage.
        var geo = lot.GeoRemoveAsync(PositionsKey, membre);
        var vu = lot.SortedSetRemoveAsync(SeenKey, membre);
        var mouvement = lot.HashDeleteAsync(MotionKey, membre);

        lot.Execute();

        await Task.WhenAll(geo, vu, mouvement).ConfigureAwait(false);
    }

    public async Task<DriverPosition?> FindAsync(Guid driverId, CancellationToken cancellationToken)
    {
        var membre = Membre(driverId);
        var db = redis.GetDatabase();

        // LES TROIS CLES DANS UN LOT : la position, son horodatage et son mouvement se
        // lisent ensemble ou la reponse n'a pas de sens.
        var lot = db.CreateBatch();

        var pointTache = lot.GeoPositionAsync(PositionsKey, membre);
        var scoreTache = lot.SortedSetScoreAsync(SeenKey, membre);
        var mouvementTache = lot.HashGetAsync(MotionKey, membre);

        lot.Execute();

        var point = await pointTache.ConfigureAwait(false);
        var score = await scoreTache.ConfigureAwait(false);
        var mouvement = await mouvementTache.ConfigureAwait(false);

        if (point is not { } coordonnees || score is null)
        {
            return null;
        }

        // UNE POSITION PERIMEE VAUT PAS DE POSITION, et la nuance ne se rend pas au
        // lecteur : « il etait la il y a une heure » n'aide personne a decider s'il
        // peut prendre la course maintenant.
        if (score.Value < Limite())
        {
            return null;
        }

        var vuA = DateTimeOffset.FromUnixTimeSeconds((long)score.Value);

        return Composer(driverId, coordonnees, vuA, mouvement);
    }

    public async Task<IReadOnlyList<NearbyDriver>> SearchAsync(
        GeoPoint center,
        int radiusMeters,
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(center);

        var db = redis.GetDatabase();

        var resultats = await db.GeoRadiusAsync(
            PositionsKey,
            center.Longitude,
            center.Latitude,
            radiusMeters,
            GeoUnit.Meters,
            limit,
            Order.Ascending,
            GeoRadiusOptions.WithCoordinates | GeoRadiusOptions.WithDistance).ConfigureAwait(false);

        if (resultats.Length == 0)
        {
            return [];
        }

        var membres = resultats.Select(r => r.Member).ToArray();

        // DEUX COMMANDES POUR TOUS LES CANDIDATS, pas deux par candidat. Interroger
        // l'horodatage et le mouvement chauffeur par chauffeur ferait deux
        // allers-retours par candidat, sur le chemin critique d'une vague.
        var lot = db.CreateBatch();
        var scoresTache = lot.SortedSetScoresAsync(SeenKey, membres);
        var mouvementsTache = lot.HashGetAsync(MotionKey, membres);
        lot.Execute();

        var scores = await scoresTache.ConfigureAwait(false);
        var mouvements = await mouvementsTache.ConfigureAwait(false);

        var limite = Limite();
        var frais = new List<NearbyDriver>(resultats.Length);

        for (var i = 0; i < resultats.Length; i++)
        {
            var score = scores[i];
            if (score is null || score.Value < limite)
            {
                continue;
            }

            var entree = resultats[i];
            if (entree.Position is not { } coordonnees || entree.Distance is not { } distance)
            {
                continue;
            }

            if (!Guid.TryParse(entree.Member.ToString(), out var driverId))
            {
                continue;
            }

            var position = Composer(
                driverId,
                coordonnees,
                DateTimeOffset.FromUnixTimeSeconds((long)score.Value),
                mouvements[i]);

            // LA DISTANCE VIENT DE REDIS, PAS DE NOTRE FORMULE. Les deux donnent le
            // meme nombre — le rayon terrestre de Distances est celui de GEORADIUS,
            // justement pour cela — mais la recalculer serait du travail pour rien
            // quand GEORADIUS l'a deja rendue.
            frais.Add(new NearbyDriver(
                position,
                (int)Math.Round(distance, MidpointRounding.AwayFromZero)));
        }

        return frais;
    }

    public async Task<IReadOnlyList<DriverPosition>> ListFreshAsync(
        int limit,
        CancellationToken cancellationToken)
    {
        var db = redis.GetDatabase();

        var plafond = limit <= 0
            ? _options.DefaultListLimit
            : Math.Clamp(limit, 1, 1000);

        // ON PART DE L'ENSEMBLE DES HORODATAGES, PAS DE L'INDEX GEO. L'index n'a pas de
        // notion de fraicheur : il rendrait aussi les positions mortes. Ici le tri par
        // score fait le travail, et « Descending » garde les plus recentes quand le
        // plafond mord.
        var recents = await db.SortedSetRangeByScoreWithScoresAsync(
            SeenKey,
            Limite(),
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

        var lot = db.CreateBatch();
        var coordonneesTache = lot.GeoPositionAsync(PositionsKey, membres);
        var mouvementsTache = lot.HashGetAsync(MotionKey, membres);
        lot.Execute();

        var coordonnees = await coordonneesTache.ConfigureAwait(false);
        var mouvements = await mouvementsTache.ConfigureAwait(false);

        var positions = new List<DriverPosition>(recents.Length);

        for (var i = 0; i < recents.Length; i++)
        {
            // UN HORODATAGE SANS COORDONNEE N'EST PAS UNE ERREUR : les trois cles se
            // nettoient dans le meme lot, mais rien ne garantit qu'un lecteur ne tombe
            // pas entre deux ecritures. On ignore, on ne leve pas — une carte a qui il
            // manque un point reste utile.
            if (coordonnees[i] is not { } point)
            {
                continue;
            }

            if (!Guid.TryParse(recents[i].Element.ToString(), out var driverId))
            {
                continue;
            }

            positions.Add(Composer(
                driverId,
                point,
                DateTimeOffset.FromUnixTimeSeconds((long)recents[i].Score),
                mouvements[i]));
        }

        return positions;
    }

    /// <summary>Horodatage en deca duquel une position ne compte plus.</summary>
    private long Limite()
        => DateTimeOffset.UtcNow.AddSeconds(-_options.FreshnessSeconds).ToUnixTimeSeconds();

    /// <summary>
    /// Recompose une position a partir des trois cles.
    /// </summary>
    ///
    /// <remarks>
    /// L'HEURE DE CAPTURE EST DANS LE MOUVEMENT, ET C'EST UN CHOIX DE RANGEMENT. Elle
    /// vient du telephone et ne decide rien ; la mettre dans l'ensemble trie, dont le
    /// score EST l'heure de reception, aurait demande une quatrieme cle pour une valeur
    /// purement informative. Quand le mouvement est absent, l'heure de capture vaut
    /// celle de reception : c'est la meilleure approximation disponible, et elle ne sert
    /// qu'a l'analyse.
    /// </remarks>
    private static DriverPosition Composer(
        Guid driverId,
        GeoPosition coordonnees,
        DateTimeOffset vuA,
        RedisValue mouvementBrut)
    {
        var (mouvement, captureA) = Decoder(mouvementBrut, vuA);

        return new DriverPosition(
            driverId,
            GeoPoint.Create(coordonnees.Latitude, coordonnees.Longitude),
            mouvement,
            captureA,
            vuA);
    }

    /// <summary>« cap;vitesse;precision;captureUnix », segments vides compris.</summary>
    private static RedisValue Encoder(Motion mouvement, DateTimeOffset capturedAt)
    {
        var champs = new[]
        {
            Nombre(mouvement.HeadingDegrees),
            Nombre(mouvement.SpeedMps),
            Nombre(mouvement.AccuracyMeters),
            capturedAt.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture),
        };

        return string.Join(Separateur, champs);
    }

    private static (Motion Mouvement, DateTimeOffset CaptureA) Decoder(
        RedisValue brut,
        DateTimeOffset repli)
    {
        if (brut.IsNullOrEmpty)
        {
            return (Motion.Inconnu, repli);
        }

        var champs = brut.ToString().Split(Separateur);

        if (champs.Length != ChampsDuMouvement)
        {
            return (Motion.Inconnu, repli);
        }

        // ON RECONSTRUIT PAR LE CONSTRUCTEUR DU DOMAINE, QUI VALIDE A NOUVEAU. Ce n'est
        // pas de la paranoia : la valeur a pu etre ecrite par une version anterieure du
        // service, ou a la main pendant un incident. Une valeur impossible redevient
        // « inconnu » plutot que de faire echouer la lecture de toute la carte.
        try
        {
            var mouvement = Motion.Create(
                Lire(champs[0]),
                Lire(champs[1]),
                Lire(champs[2]));

            var captureA = long.TryParse(champs[3], CultureInfo.InvariantCulture, out var unix)
                ? DateTimeOffset.FromUnixTimeSeconds(unix)
                : repli;

            return (mouvement, captureA);
        }
        catch (Hba.BuildingBlocks.Domain.DomainException)
        {
            return (Motion.Inconnu, repli);
        }
    }

    private static string Nombre(double? valeur)
        => valeur?.ToString("R", CultureInfo.InvariantCulture) ?? string.Empty;

    private static double? Lire(string champ)
        => double.TryParse(champ, NumberStyles.Float, CultureInfo.InvariantCulture, out var valeur)
            ? valeur
            : null;

    private static RedisValue Membre(Guid driverId) => driverId.ToString("D", CultureInfo.InvariantCulture);
}
