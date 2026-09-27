using System.Data;
using System.Data.Common;
using Hba.BuildingBlocks.Application.Time;
using Hba.Driver.Application.Common.Interfaces;
using Hba.Driver.Domain.Drivers;
using Microsoft.EntityFrameworkCore;

namespace Hba.Driver.Infrastructure.Persistence.Reads;

/// <summary>
/// Les effectifs, en SQL.
///
/// LES DEUX PREMIERES REQUETES N'ONT PAS DE FENETRE, ET C'EST VOULU. Un
/// livreur n'est pas disponible « en septembre », il l'est maintenant :
/// filtrer un etat par une periode ne repondrait a aucune question.
/// </summary>
internal sealed class DriverStatsReader(DriverDbContext context) : IDriverStatsReader
{
    private const string Table = "driver.drivers";

    public async Task<DriverStatsView> ReadAsync(TimeWindow window, CancellationToken cancellationToken)
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
            var verification = await VerificationAsync(connexion, cancellationToken).ConfigureAwait(false);
            var operationnel = await OperationnelAsync(connexion, cancellationToken).ConfigureAwait(false);
            var flux = await FluxAsync(connexion, window, cancellationToken).ConfigureAwait(false);

            return new DriverStatsView(
                window,
                verification.Sum(t => t.Count),
                verification,
                operationnel,
                flux.Inscrits,
                flux.Valides);
        }
        finally
        {
            if (ouverteParNous)
            {
                await context.Database.CloseConnectionAsync().ConfigureAwait(false);
            }
        }
    }

    private static async Task<IReadOnlyList<VerificationTally>> VerificationAsync(
        DbConnection connexion,
        CancellationToken cancellationToken)
    {
        const string sql = $"""
            SELECT verification_status, count(*) AS nombre
              FROM {Table}
             GROUP BY verification_status
            """;

        await using var commande = Commande(connexion, sql);
        await using var lecteur = await commande.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        var resultats = new List<VerificationTally>();

        while (await lecteur.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            resultats.Add(new VerificationTally((VerificationStatus)lecteur.GetInt32(0), lecteur.GetInt64(1)));
        }

        return resultats;
    }

    private static async Task<IReadOnlyList<OperationalTally>> OperationnelAsync(
        DbConnection connexion,
        CancellationToken cancellationToken)
    {
        const string sql = $"""
            SELECT operational_status, count(*) AS nombre
              FROM {Table}
             GROUP BY operational_status
            """;

        await using var commande = Commande(connexion, sql);
        await using var lecteur = await commande.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        var resultats = new List<OperationalTally>();

        while (await lecteur.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            resultats.Add(new OperationalTally((OperationalStatus)lecteur.GetInt32(0), lecteur.GetInt64(1)));
        }

        return resultats;
    }

    private static async Task<(long Inscrits, long Valides)> FluxAsync(
        DbConnection connexion,
        TimeWindow window,
        CancellationToken cancellationToken)
    {
        // Une seule passe : deux compteurs filtres valent mieux que deux
        // requetes sur la meme table.
        const string sql = $"""
            SELECT count(*) FILTER (WHERE registered_at >= @debut AND registered_at < @fin) AS inscrits,
                   count(*) FILTER (WHERE verified_at IS NOT NULL
                                      AND verified_at >= @debut AND verified_at < @fin) AS valides
              FROM {Table}
            """;

        await using var commande = Commande(connexion, sql);
        Ajouter(commande, "debut", window.From);
        Ajouter(commande, "fin", window.To);

        await using var lecteur = await commande.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        return await lecteur.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? (lecteur.GetInt64(0), lecteur.GetInt64(1))
            : (0, 0);
    }

    private static DbCommand Commande(DbConnection connexion, string sql)
    {
        var commande = connexion.CreateCommand();
        commande.CommandText = sql;
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
