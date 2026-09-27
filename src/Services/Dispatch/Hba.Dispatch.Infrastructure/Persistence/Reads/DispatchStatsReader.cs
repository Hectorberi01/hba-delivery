using System.Data;
using System.Data.Common;
using Hba.BuildingBlocks.Application.Time;
using Hba.Dispatch.Application.Common.Interfaces;
using Hba.Dispatch.Domain.Dispatching;
using Microsoft.EntityFrameworkCore;

namespace Hba.Dispatch.Infrastructure.Persistence.Reads;

/// <summary>
/// La mesure de l'affectation, en SQL.
///
/// DEUX FENETRES SUR DEUX TABLES, ET ELLES NE COMPTENT PAS LA MEME CHOSE.
/// Les recherches se lisent par date d'ouverture, les offres par date
/// d'envoi. Une recherche ouverte a 23 h 58 et dont la troisieme vague part
/// le lendemain appartient a la veille pour la recherche, au lendemain pour
/// cette offre-la. C'est la realite du moteur, pas un defaut de comptage.
/// </summary>
internal sealed class DispatchStatsReader(DispatchDbContext context) : IDispatchStatsReader
{
    private const string Dispatches = "dispatch.dispatches";
    private const string Offers = "dispatch.offers";

    public async Task<DispatchStatsView> ReadAsync(TimeWindow window, CancellationToken cancellationToken)
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
            var offres = await OffresAsync(connexion, window, cancellationToken).ConfigureAwait(false);
            var vagues = await VaguesAsync(connexion, window, cancellationToken).ConfigureAwait(false);
            var affectation = await DelaiAffectationAsync(connexion, window, cancellationToken).ConfigureAwait(false);
            var reponse = await DelaiReponseAsync(connexion, window, cancellationToken).ConfigureAwait(false);

            return new DispatchStatsView(window, parStatut, offres, vagues, affectation, reponse);
        }
        finally
        {
            if (ouverteParNous)
            {
                await context.Database.CloseConnectionAsync().ConfigureAwait(false);
            }
        }
    }

    private static async Task<IReadOnlyList<DispatchStatusTally>> ParStatutAsync(
        DbConnection connexion,
        TimeWindow window,
        CancellationToken cancellationToken)
    {
        const string sql = $"""
            SELECT status, count(*) AS nombre
              FROM {Dispatches}
             WHERE created_at >= @debut AND created_at < @fin
             GROUP BY status
            """;

        await using var commande = Commande(connexion, sql, window);
        await using var lecteur = await commande.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        var resultats = new List<DispatchStatusTally>();

        while (await lecteur.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            resultats.Add(new DispatchStatusTally((DispatchStatus)lecteur.GetInt32(0), lecteur.GetInt64(1)));
        }

        return resultats;
    }

    private static async Task<IReadOnlyList<OfferStatusTally>> OffresAsync(
        DbConnection connexion,
        TimeWindow window,
        CancellationToken cancellationToken)
    {
        const string sql = $"""
            SELECT status, count(*) AS nombre
              FROM {Offers}
             WHERE sent_at >= @debut AND sent_at < @fin
             GROUP BY status
            """;

        await using var commande = Commande(connexion, sql, window);
        await using var lecteur = await commande.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        var resultats = new List<OfferStatusTally>();

        while (await lecteur.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            resultats.Add(new OfferStatusTally((OfferStatus)lecteur.GetInt32(0), lecteur.GetInt64(1)));
        }

        return resultats;
    }

    private static async Task<IReadOnlyList<WaveTally>> VaguesAsync(
        DbConnection connexion,
        TimeWindow window,
        CancellationToken cancellationToken)
    {
        const string sql = $"""
            SELECT wave_number, count(*) AS nombre
              FROM {Offers}
             WHERE sent_at >= @debut AND sent_at < @fin
               AND status = @acceptee
             GROUP BY wave_number
             ORDER BY wave_number
            """;

        await using var commande = Commande(connexion, sql, window);
        Ajouter(commande, "acceptee", (int)OfferStatus.Accepted);

        await using var lecteur = await commande.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        var resultats = new List<WaveTally>();

        while (await lecteur.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            resultats.Add(new WaveTally(lecteur.GetInt32(0), lecteur.GetInt64(1)));
        }

        return resultats;
    }

    private static async Task<DurationTally> DelaiAffectationAsync(
        DbConnection connexion,
        TimeWindow window,
        CancellationToken cancellationToken)
    {
        const string sql = $"""
            SELECT coalesce(avg(extract(epoch from (closed_at - created_at)))
                            FILTER (WHERE status = @affectee AND closed_at IS NOT NULL), 0)::int AS moyenne,
                   count(*) FILTER (WHERE status = @affectee AND closed_at IS NOT NULL) AS effectif
              FROM {Dispatches}
             WHERE created_at >= @debut AND created_at < @fin
            """;

        await using var commande = Commande(connexion, sql, window);
        Ajouter(commande, "affectee", (int)DispatchStatus.Assigned);

        return await LireDureeAsync(commande, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<DurationTally> DelaiReponseAsync(
        DbConnection connexion,
        TimeWindow window,
        CancellationToken cancellationToken)
    {
        // Le delai d'acceptation, pas le delai de reponse en general : une
        // offre refusee ou expiree ne dit rien de la vitesse a laquelle un
        // livreur prend une course.
        const string sql = $"""
            SELECT coalesce(avg(extract(epoch from (resolved_at - sent_at)))
                            FILTER (WHERE status = @acceptee AND resolved_at IS NOT NULL), 0)::int AS moyenne,
                   count(*) FILTER (WHERE status = @acceptee AND resolved_at IS NOT NULL) AS effectif
              FROM {Offers}
             WHERE sent_at >= @debut AND sent_at < @fin
            """;

        await using var commande = Commande(connexion, sql, window);
        Ajouter(commande, "acceptee", (int)OfferStatus.Accepted);

        return await LireDureeAsync(commande, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<DurationTally> LireDureeAsync(
        DbCommand commande,
        CancellationToken cancellationToken)
    {
        await using var lecteur = await commande.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        return await lecteur.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? new DurationTally(lecteur.GetInt32(0), lecteur.GetInt64(1))
            : new DurationTally(0, 0);
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
