using System.Data;
using System.Data.Common;
using System.Globalization;
using Hba.BuildingBlocks.Application.Time;
using Hba.Payment.Application.Common.Interfaces;
using Hba.Payment.Domain.Payments;
using Microsoft.EntityFrameworkCore;

namespace Hba.Payment.Infrastructure.Persistence.Reads;

/// <summary>
/// L'argent, en SQL, sur la base de Payment.
///
/// LA COHORTE SE LIT SUR created_at, LA RECETTE SUR succeeded_at. Les deux
/// fenêtres portent les mêmes bornes et ne rendent pas les mêmes lignes :
/// c'est voulu, et c'est documenté dans le contrat. Prendre created_at pour
/// la recette ferait glisser d'un mois tout paiement arrivé le lendemain.
/// </summary>
internal sealed class PaymentStatsReader(PaymentDbContext context, ITimeCalendar calendrier)
    : IPaymentStatsReader
{
    private const string Table = "payment.payment_intents";

    public async Task<PaymentStatsView> ReadAsync(
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
            var parMethode = await ParMethodeAsync(connexion, window, cancellationToken).ConfigureAwait(false);
            var recette = await RecetteAsync(connexion, window, granularity, cancellationToken).ConfigureAwait(false);
            var delai = await DelaiAsync(connexion, window, cancellationToken).ConfigureAwait(false);

            return new PaymentStatsView(
                window,
                parStatut,
                parMethode,
                recette.Sum(p => p.Count),
                recette.Sum(p => p.AmountXof),
                recette,
                delai.Moyenne,
                delai.Effectif);
        }
        finally
        {
            if (ouverteParNous)
            {
                await context.Database.CloseConnectionAsync().ConfigureAwait(false);
            }
        }
    }

    private static async Task<IReadOnlyList<PaymentStatusTally>> ParStatutAsync(
        DbConnection connexion,
        TimeWindow window,
        CancellationToken cancellationToken)
    {
        const string sql = $"""
            SELECT status,
                   count(*) AS nombre,
                   coalesce(sum(amount), 0) AS montant
              FROM {Table}
             WHERE created_at >= @debut AND created_at < @fin
             GROUP BY status
            """;

        await using var commande = Commande(connexion, sql, window);
        await using var lecteur = await commande.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        var resultats = new List<PaymentStatusTally>();

        while (await lecteur.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            resultats.Add(new PaymentStatusTally(
                (PaymentStatus)lecteur.GetInt32(0),
                lecteur.GetInt64(1),
                Entier(lecteur.GetValue(2))));
        }

        return resultats;
    }

    private static async Task<IReadOnlyList<PaymentMethodTally>> ParMethodeAsync(
        DbConnection connexion,
        TimeWindow window,
        CancellationToken cancellationToken)
    {
        const string sql = $"""
            SELECT method,
                   count(*) AS nombre,
                   coalesce(sum(amount), 0) AS montant
              FROM {Table}
             WHERE created_at >= @debut AND created_at < @fin
             GROUP BY method
            """;

        await using var commande = Commande(connexion, sql, window);
        await using var lecteur = await commande.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        var resultats = new List<PaymentMethodTally>();

        while (await lecteur.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            resultats.Add(new PaymentMethodTally(
                (PaymentMethod)lecteur.GetInt32(0),
                lecteur.GetInt64(1),
                Entier(lecteur.GetValue(2))));
        }

        return resultats;
    }

    private async Task<IReadOnlyList<PaymentSeriesTally>> RecetteAsync(
        DbConnection connexion,
        TimeWindow window,
        TimeGranularity granularity,
        CancellationToken cancellationToken)
    {
        // Le filtre porte sur succeeded_at, et le statut est vérifié en plus :
        // une intention ne devrait jamais porter une date d'encaissement sans
        // être payée, mais compter de l'argent sur cette seule promesse serait
        // imprudent.
        const string sql = $"""
            SELECT to_char(succeeded_at AT TIME ZONE @zone, @format) AS cle,
                   count(*) AS nombre,
                   coalesce(sum(amount), 0) AS montant
              FROM {Table}
             WHERE succeeded_at IS NOT NULL
               AND succeeded_at >= @debut AND succeeded_at < @fin
               AND status = @payee
             GROUP BY 1
            """;

        await using var commande = Commande(connexion, sql, window);
        Ajouter(commande, "zone", calendrier.ZoneId);
        Ajouter(commande, "format", granularity == TimeGranularity.Month ? "YYYY-MM" : "YYYY-MM-DD");
        Ajouter(commande, "payee", (int)PaymentStatus.Succeeded);

        await using var lecteur = await commande.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        var resultats = new List<PaymentSeriesTally>();

        while (await lecteur.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            resultats.Add(new PaymentSeriesTally(
                lecteur.GetString(0),
                lecteur.GetInt64(1),
                Entier(lecteur.GetValue(2))));
        }

        return resultats;
    }

    private static async Task<(int Moyenne, long Effectif)> DelaiAsync(
        DbConnection connexion,
        TimeWindow window,
        CancellationToken cancellationToken)
    {
        const string sql = $"""
            SELECT coalesce(avg(extract(epoch from (succeeded_at - created_at)))
                            FILTER (WHERE succeeded_at IS NOT NULL), 0)::int AS moyenne,
                   count(*) FILTER (WHERE succeeded_at IS NOT NULL) AS effectif
              FROM {Table}
             WHERE created_at >= @debut AND created_at < @fin
            """;

        await using var commande = Commande(connexion, sql, window);
        await using var lecteur = await commande.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        return await lecteur.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? (lecteur.GetInt32(0), lecteur.GetInt64(1))
            : (0, 0);
    }

    /// <summary>sum() d'un bigint rend un numeric : Npgsql le remonte en decimal.</summary>
    private static long Entier(object valeur) => Convert.ToInt64(valeur, CultureInfo.InvariantCulture);

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
