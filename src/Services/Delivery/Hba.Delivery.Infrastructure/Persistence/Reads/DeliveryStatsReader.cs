using System.Data;
using System.Data.Common;
using Hba.BuildingBlocks.Application.Time;
using Hba.Delivery.Application.Ports;
using Hba.Delivery.Domain.Deliveries;
using Microsoft.EntityFrameworkCore;

namespace Hba.Delivery.Infrastructure.Persistence.Reads;

/// <summary>
/// Les agrégats, en SQL, sur la base de Delivery.
///
/// PAS DE LINQ ICI, ET C'EST DELIBERE. Le découpage par journée LOCALE
/// (ADR 0019) passe par « AT TIME ZONE », qu'aucun fournisseur EF ne traduit ;
/// et les délais moyens se lisent d'une seule passe avec des agrégats filtrés.
/// Ecrit en LINQ, tout cela deviendrait soit faux — journées coupées en UTC —
/// soit quatre fois plus de requêtes.
///
/// Les noms de colonnes suivent la cartographie EF telle qu'elle est : les
/// propriétés directes de l'agrégat gardent leur casse d'origine et se citent
/// entre guillemets, les types possédés sont en minuscules.
/// </summary>
internal sealed class DeliveryStatsReader(DeliveryDbContext context, ITimeCalendar calendrier)
    : IDeliveryStatsReader
{
    private const string Table = "delivery.deliveries";

    public async Task<DeliveryStatsView> ReadAsync(
        TimeWindow window,
        TimeGranularity granularity,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(window);

        var connexion = context.Database.GetDbConnection();
        var ouverteParNous = connexion.State != ConnectionState.Open;

        if (ouverteParNous)
        {
            await context.Database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        }

        try
        {
            var parStatut = await ParStatutAsync(connexion, window, cancellationToken).ConfigureAwait(false);
            var parSource = await ParSourceAsync(connexion, window, cancellationToken).ConfigureAwait(false);
            var serie = await SerieAsync(connexion, window, granularity, cancellationToken).ConfigureAwait(false);
            var delais = await DelaisAsync(connexion, window, cancellationToken).ConfigureAwait(false);

            return new DeliveryStatsView(window, parStatut, parSource, serie, delais.A, delais.B, delais.C);
        }
        finally
        {
            // On ne ferme que ce qu'on a ouvert : la connexion peut appartenir
            // à une transaction en cours, et la refermer la casserait.
            if (ouverteParNous)
            {
                await context.Database.CloseConnectionAsync().ConfigureAwait(false);
            }
        }
    }

    private static async Task<IReadOnlyList<StatusTally>> ParStatutAsync(
        DbConnection connexion,
        TimeWindow window,
        CancellationToken cancellationToken)
    {
        const string sql = $"""
            SELECT "Status" AS statut,
                   count(*) AS nombre,
                   coalesce(sum(price_total_xof), 0) AS montant
              FROM {Table}
             WHERE "CreatedAt" >= @debut AND "CreatedAt" < @fin
             GROUP BY "Status"
            """;

        await using var commande = Commande(connexion, sql, window);
        await using var lecteur = await commande.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        var resultats = new List<StatusTally>();

        while (await lecteur.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            resultats.Add(new StatusTally(
                (DeliveryStatus)lecteur.GetInt32(0),
                lecteur.GetInt64(1),
                Convert.ToInt64(lecteur.GetValue(2), System.Globalization.CultureInfo.InvariantCulture)));
        }

        return resultats;
    }

    private static async Task<IReadOnlyList<SourceTally>> ParSourceAsync(
        DbConnection connexion,
        TimeWindow window,
        CancellationToken cancellationToken)
    {
        const string sql = $"""
            SELECT "Source" AS source, count(*) AS nombre
              FROM {Table}
             WHERE "CreatedAt" >= @debut AND "CreatedAt" < @fin
             GROUP BY "Source"
            """;

        await using var commande = Commande(connexion, sql, window);
        await using var lecteur = await commande.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        var resultats = new List<SourceTally>();

        while (await lecteur.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            resultats.Add(new SourceTally((DeliverySource)lecteur.GetInt32(0), lecteur.GetInt64(1)));
        }

        return resultats;
    }

    private async Task<IReadOnlyList<SeriesTally>> SerieAsync(
        DbConnection connexion,
        TimeWindow window,
        TimeGranularity granularity,
        CancellationToken cancellationToken)
    {
        const string sql = $"""
            SELECT to_char("CreatedAt" AT TIME ZONE @zone, @format) AS cle,
                   count(*) AS nombre
              FROM {Table}
             WHERE "CreatedAt" >= @debut AND "CreatedAt" < @fin
             GROUP BY 1
            """;

        await using var commande = Commande(connexion, sql, window);
        Ajouter(commande, "zone", calendrier.ZoneId);
        Ajouter(commande, "format", granularity == TimeGranularity.Month ? "YYYY-MM" : "YYYY-MM-DD");

        await using var lecteur = await commande.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        var resultats = new List<SeriesTally>();

        while (await lecteur.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            resultats.Add(new SeriesTally(lecteur.GetString(0), lecteur.GetInt64(1)));
        }

        return resultats;
    }

    private static async Task<(DurationTally A, DurationTally B, DurationTally C)> DelaisAsync(
        DbConnection connexion,
        TimeWindow window,
        CancellationToken cancellationToken)
    {
        // Les moyennes sont filtrées par agrégat plutôt que par trois requêtes :
        // une course sans affectation ne doit pas peser zéro dans la moyenne,
        // elle doit en être absente.
        const string sql = $"""
            SELECT
                coalesce(avg(extract(epoch from ("AssignedAt" - "CreatedAt")))
                         FILTER (WHERE "AssignedAt" IS NOT NULL), 0)::int AS affectation_moyenne,
                count(*) FILTER (WHERE "AssignedAt" IS NOT NULL) AS affectation_n,
                coalesce(avg(extract(epoch from ("PickedUpAt" - "CreatedAt")))
                         FILTER (WHERE "PickedUpAt" IS NOT NULL), 0)::int AS collecte_moyenne,
                count(*) FILTER (WHERE "PickedUpAt" IS NOT NULL) AS collecte_n,
                coalesce(avg(extract(epoch from ("CompletedAt" - "CreatedAt")))
                         FILTER (WHERE "Status" = @livree), 0)::int AS livraison_moyenne,
                count(*) FILTER (WHERE "Status" = @livree) AS livraison_n
              FROM {Table}
             WHERE "CreatedAt" >= @debut AND "CreatedAt" < @fin
            """;

        await using var commande = Commande(connexion, sql, window);
        Ajouter(commande, "livree", (int)DeliveryStatus.Delivered);

        await using var lecteur = await commande.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        if (!await lecteur.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var vide = new DurationTally(0, 0);
            return (vide, vide, vide);
        }

        return (
            new DurationTally(lecteur.GetInt32(0), lecteur.GetInt64(1)),
            new DurationTally(lecteur.GetInt32(2), lecteur.GetInt64(3)),
            new DurationTally(lecteur.GetInt32(4), lecteur.GetInt64(5)));
    }

    private static DbCommand Commande(DbConnection connexion, string sql, TimeWindow window)
    {
        var commande = connexion.CreateCommand();
        commande.CommandText = sql;

        Ajouter(commande, "debut", window.From);
        Ajouter(commande, "fin", window.To);

        return commande;
    }

    private static void Ajouter(DbCommand commande, string nom, object valeur)
    {
        var parametre = commande.CreateParameter();
        parametre.ParameterName = nom;
        parametre.Value = valeur;
        commande.Parameters.Add(parametre);
    }
}
