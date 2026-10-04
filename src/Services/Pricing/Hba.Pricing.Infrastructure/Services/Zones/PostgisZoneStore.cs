using System.Data.Common;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Hba.BuildingBlocks.Domain;
using Hba.Pricing.Application.Common.Interfaces;
using Hba.Pricing.Domain.Exceptions;
using Hba.Pricing.Domain.ValueObjects;
using Hba.Pricing.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Hba.Pricing.Infrastructure.Services.Zones;

/// <summary>
/// Les zones, et la seule geometrie du depot.
/// </summary>
///
/// <remarks>
/// TOUTE LA GEOMETRIE EST ICI, EN SQL, ET NULLE PART AILLEURS. C'est ce que
/// l'agregat Zone annonce depuis sa premiere ligne : le polygone vit dans une
/// colonne geography et le domaine ne fait pas de geometrie. Ce fichier remplace
/// ConfiguredZoneLocator, qui rendait la zone de repli configuree pour TOUT point
/// du globe — le zonage tarifaire etait fictif, et une seule grille s'appliquait
/// partout.
///
/// ST_Covers ET NON ST_Contains, ET CE N'EST PAS UN DETAIL. ST_Contains n'existe
/// pas pour le type geography : seuls ST_Covers et ST_Intersects y sont definis.
/// La difference porte sur la frontiere — ST_Covers accepte un point POSE SUR le
/// bord, ST_Contains l'exclut — et accepter le bord est ce qu'on veut : deux zones
/// mitoyennes ne doivent pas laisser une bande d'un metre sans prix entre elles.
/// Le chevauchement, lui, est departage par la priorite.
///
/// DU SQL BRUT, PAS DE LINQ, ET PAS DE NetTopologySuite. Traduire ST_Covers en
/// LINQ demanderait Npgsql.EntityFrameworkCore.PostgreSQL.NetTopologySuite, donc
/// un type de geometrie qui remonterait jusqu'au modele EF — et le polygone
/// cesserait d'etre l'affaire de la seule requete. Trois commandes SQL de dix
/// lignes coutent moins cher que cette dependance.
///
/// LES COORDONNEES PARTENT EN POINT DECIMAL, TOUJOURS. Une machine configuree en
/// francais ecrirait « 6,3654 » dans le WKT, que PostGIS lirait comme deux
/// nombres : la course serait placee au large du Ghana, ou refusee. D'ou
/// InvariantCulture a chaque conversion.
/// </remarks>
internal sealed class PostgisZoneStore(PricingDbContext context, IOptions<ZoneOptions> options)
    : IZoneLocator, IZoneRingStore
{
    /// <summary>
    /// Huit decimales valent environ un millimetre a l'equateur : bien au-dela de
    /// ce qu'un doigt sur une carte peut designer, et sans notation scientifique,
    /// que le lecteur WKT n'accepte pas.
    /// </summary>
    private const string FormatCoordonnee = "0.########";

    public async Task<string?> ResolveAsync(GeoPoint point, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(point);

        const string requete = """
            SELECT code
            FROM pricing.zones
            WHERE is_active
              AND area IS NOT NULL
              AND ST_Covers(area, ST_SetSRID(ST_MakePoint(@lng, @lat), 4326)::geography)
            ORDER BY priority DESC, code
            LIMIT 1
            """;

        using var commande = await CommandeAsync(requete, cancellationToken).ConfigureAwait(false);

        Ajouter(commande, "lng", point.Longitude);
        Ajouter(commande, "lat", point.Latitude);

        var trouve = await AvecDiagnostic(() => commande.ExecuteScalarAsync(cancellationToken))
            .ConfigureAwait(false);

        if (trouve is string code && !string.IsNullOrWhiteSpace(code))
        {
            return code;
        }

        // LE REPLI NE SERT PLUS QU'A UN BANC DE DEVELOPPEMENT, et il reste pour
        // cela : tant qu'aucune zone n'est dessinee, une valeur non vide fait
        // entrer le monde entier dans cette zone. VIDE EN PRODUCTION, ou un point
        // hors de toute zone doit etre refuse (ZONE_NOT_COVERED) plutot que
        // facture au tarif d'une ville ou il n'est pas.
        var repli = options.Value.FallbackZoneCode;

        return string.IsNullOrWhiteSpace(repli) ? null : repli;
    }

    public async Task<bool> IsValidAsync(
        IReadOnlyList<GeoPoint> ring,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ring);

        const string requete = "SELECT ST_IsValid(ST_GeomFromText(@wkt, 4326))";

        using var commande = await CommandeAsync(requete, cancellationToken).ConfigureAwait(false);

        Ajouter(commande, "wkt", Wkt(ring));

        var verdict = await AvecDiagnostic(() => commande.ExecuteScalarAsync(cancellationToken))
            .ConfigureAwait(false);

        return verdict is true;
    }

    public async Task SaveAsync(
        Guid zoneId,
        IReadOnlyList<GeoPoint> ring,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ring);

        const string requete = """
            UPDATE pricing.zones
            SET area = ST_GeogFromText(@wkt)
            WHERE id = @id
            """;

        using var commande = await CommandeAsync(requete, cancellationToken).ConfigureAwait(false);

        Ajouter(commande, "wkt", Wkt(ring));
        Ajouter(commande, "id", zoneId);

        await AvecDiagnostic(() => commande.ExecuteNonQueryAsync(cancellationToken))
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyDictionary<Guid, IReadOnlyList<GeoPoint>>> ReadAsync(
        IReadOnlyList<Guid> zoneIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(zoneIds);

        var contours = new Dictionary<Guid, IReadOnlyList<GeoPoint>>();

        if (zoneIds.Count == 0)
        {
            return contours;
        }

        // GeoJSON PLUTOT QUE WKT, PARCE QU'IL SE LIT SANS ANALYSEUR. ST_AsText
        // rendrait « POLYGON((...)) » qu'il faudrait decouper a la main ; le
        // GeoJSON se lit avec System.Text.Json, deja present partout.
        const string requete = """
            SELECT id, ST_AsGeoJSON(area)
            FROM pricing.zones
            WHERE id = ANY(@ids) AND area IS NOT NULL
            """;

        using var commande = await CommandeAsync(requete, cancellationToken).ConfigureAwait(false);

        Ajouter(commande, "ids", zoneIds.ToArray());

        await using var lecteur = await AvecDiagnostic(
            () => commande.ExecuteReaderAsync(cancellationToken)).ConfigureAwait(false);

        while (await lecteur.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var id = lecteur.GetGuid(0);

            if (lecteur.IsDBNull(1))
            {
                continue;
            }

            var points = Lire(lecteur.GetString(1));

            if (points.Count > 0)
            {
                contours[id] = points;
            }
        }

        return contours;
    }

    /// <summary>
    /// « POLYGON((lng lat, lng lat, ..., lng lat)) », contour ferme.
    /// </summary>
    ///
    /// <remarks>
    /// LE PREMIER POINT EST REPETE A LA FIN, PARCE QUE LE WKT L'EXIGE — un anneau
    /// non ferme est refuse. Les appelants, eux, n'ont pas a le savoir : une carte
    /// rend le trace, pas sa fermeture.
    ///
    /// L'ORDRE DES SOMMETS N'EST PAS CORRIGE. PostGIS accepte les deux sens pour un
    /// polygone simple ; ce qu'il refuse, c'est un contour qui se recoupe, et c'est
    /// ST_IsValid qui le dit.
    /// </remarks>
    private static string Wkt(IReadOnlyList<GeoPoint> ring)
    {
        var texte = new StringBuilder("POLYGON((");

        for (var i = 0; i < ring.Count; i += 1)
        {
            if (i > 0)
            {
                texte.Append(", ");
            }

            Ecrire(texte, ring[i]);
        }

        // La fermeture, sauf si l'appelant l'a deja posee.
        if (ring.Count > 0 && !ring[0].Equals(ring[^1]))
        {
            texte.Append(", ");
            Ecrire(texte, ring[0]);
        }

        return texte.Append("))").ToString();
    }

    private static void Ecrire(StringBuilder texte, GeoPoint point) =>
        texte
            .Append(point.Longitude.ToString(FormatCoordonnee, CultureInfo.InvariantCulture))
            .Append(' ')
            .Append(point.Latitude.ToString(FormatCoordonnee, CultureInfo.InvariantCulture));

    /// <summary>
    /// Les sommets d'un GeoJSON de polygone, anneau exterieur seulement.
    /// </summary>
    ///
    /// <remarks>
    /// LES TROUS SONT IGNORES, ET AUCUN N'EST CREE. Un GeoJSON de polygone porte un
    /// tableau d'anneaux dont le premier est le contour et les suivants des trous ;
    /// rien dans ce depot n'en dessine, et en rendre un a un ecran qui n'en attend
    /// pas dessinerait une zone fausse.
    ///
    /// LA FERMETURE EST RETIREE. La base rend toujours l'anneau ferme — premier
    /// point repete — ce qui afficherait un sommet en double dans un editeur.
    /// </remarks>
    private static IReadOnlyList<GeoPoint> Lire(string geojson)
    {
        using var document = JsonDocument.Parse(geojson);

        if (!document.RootElement.TryGetProperty("coordinates", out var anneaux)
            || anneaux.ValueKind != JsonValueKind.Array
            || anneaux.GetArrayLength() == 0)
        {
            return [];
        }

        var exterieur = anneaux[0];

        if (exterieur.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var points = new List<GeoPoint>();

        foreach (var sommet in exterieur.EnumerateArray())
        {
            if (sommet.ValueKind != JsonValueKind.Array || sommet.GetArrayLength() < 2)
            {
                continue;
            }

            // GeoJSON ECRIT LA LONGITUDE EN PREMIER, contrairement a tout le reste
            // de ce depot. Les inverser placerait Cotonou en Somalie.
            points.Add(GeoPoint.Create(sommet[1].GetDouble(), sommet[0].GetDouble()));
        }

        if (points.Count > 1 && points[0].Equals(points[^1]))
        {
            points.RemoveAt(points.Count - 1);
        }

        return points;
    }

    /// <summary>
    /// Traduit l'absence de schema geographique en refus nomme.
    /// </summary>
    ///
    /// <remarks>
    /// SANS CELA, UNE BASE NON MIGREE REPOND 500 ET NE DIT RIEN. Database:AutoMigrate
    /// vaut faux dans ce deploiement : la colonne « area » et l'extension postgis
    /// n'existent qu'apres « make migrate ». L'oubli est donc un etat normal d'un
    /// environnement neuf, et la console affichait « Internal Server Error » sans que
    /// rien ne nomme la migration manquante.
    ///
    /// TROIS ETATS SQL, ET PAS UN DE PLUS. 42703 : la colonne manque. 42883 : la
    /// fonction manque, c'est-a-dire l'extension. 42P01 : la table manque, donc aucune
    /// migration n'a jamais tourne. Tout le reste remonte tel quel — une panne de
    /// base n'est pas un refus metier, et la deguiser en 400 ferait chercher un
    /// contour fautif la ou le serveur est tombe.
    /// </remarks>
    private static async Task<T> AvecDiagnostic<T>(Func<Task<T>> execution)
    {
        try
        {
            return await execution().ConfigureAwait(false);
        }
        catch (PostgresException erreur) when (erreur.SqlState is "42703" or "42883" or "42P01")
        {
            throw new DomainException(
                PricingErrorCodes.ZoneSchemaMissing,
                "Le schema geographique des zones n'est pas en place : la colonne "
                + "« area » ou l'extension postgis manque dans la base de Pricing. "
                + "Appliquez la migration ZonesGeographiques — « make migrate ».");
        }
    }

    private async Task<DbCommand> CommandeAsync(string requete, CancellationToken cancellationToken)
    {
        // LA CONNEXION EST CELLE DU CONTEXTE, et c'est ce qui compte : EF l'ouvre
        // une fois et la garde, donc ces commandes voient les ecritures deja
        // enregistrees dans la meme unite de travail.
        var connexion = context.Database.GetDbConnection();

        if (connexion.State != System.Data.ConnectionState.Open)
        {
            await context.Database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        }

        var commande = connexion.CreateCommand();
        commande.CommandText = requete;

        var transaction = context.Database.CurrentTransaction;

        if (transaction is not null)
        {
            commande.Transaction = transaction.GetDbTransaction();
        }

        return commande;
    }

    /// <summary>
    /// TOUT PASSE PAR UN PARAMETRE, Y COMPRIS LE WKT. Il est construit ici a partir
    /// de nombres, donc rien d'injectable n'y entre — mais le concatener prendrait
    /// l'habitude, et la prochaine chaine viendrait d'ailleurs.
    /// </summary>
    private static void Ajouter(DbCommand commande, string nom, object valeur)
    {
        var parametre = commande.CreateParameter();
        parametre.ParameterName = nom;
        parametre.Value = valeur;
        commande.Parameters.Add(parametre);
    }
}
