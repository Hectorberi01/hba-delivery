using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Hba.BuildingBlocks.Persistence;

/// <summary>
/// Combien de temps le journal des lectures est conservé.
///
/// AUCUNE VALEUR PAR DÉFAUT, ET C'EST DÉLIBÉRÉ. La durée de conservation
/// d'une trace d'accès à des données personnelles est une décision juridique,
/// pas une décision d'ingénierie : écrire « 24 mois » ici en ferait une règle
/// arrêtée alors qu'elle serait arbitraire. Tant que le réglage est absent,
/// la purge ne tourne pas — et le dit au démarrage, pour que le silence ne
/// passe pas pour un fonctionnement.
/// </summary>
public sealed class PersonalDataReadRetentionOptions
{
    public const string SectionName = "PersonalDataReads";

    /// <summary>Âge au-delà duquel une ligne est supprimée. Zéro : rien n'est purgé.</summary>
    public int RetentionDays { get; set; }
}

/// <summary>
/// Supprime les lignes du journal passé leur durée de conservation.
///
/// UNE SEULE INSTRUCTION, PAS UN CHARGEMENT. ExecuteDelete supprime en base
/// sans matérialiser une ligne : un journal de plusieurs centaines de
/// milliers d'entrées ne doit pas traverser la mémoire du service pour être
/// oublié.
/// </summary>
public sealed class PersonalDataReadPurge<TContext>(
    IServiceScopeFactory scopes,
    IOptions<PersonalDataReadRetentionOptions> options,
    ILogger<PersonalDataReadPurge<TContext>> journal) : BackgroundService
    where TContext : DbContext
{
    private static readonly TimeSpan Intervalle = TimeSpan.FromHours(24);

    /// <summary>
    /// LE SERVICE NE DEMARRE PAS SA PURGE TOUT DE SUITE : un redémarrage en
    /// boucle lancerait une suppression à chaque tentative, sur une base qui
    /// finit peut-être ses migrations.
    /// </summary>
    private static readonly TimeSpan Delai = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var jours = options.Value.RetentionDays;

        if (jours <= 0)
        {
            journal.LogInformation(
                "Journal des lectures : aucune purge. {Section}:RetentionDays n'est pas renseigne, "
                + "donc les traces d'acces sont conservees indefiniment.",
                PersonalDataReadRetentionOptions.SectionName);

            return;
        }

        try
        {
            await Task.Delay(Delai, stoppingToken).ConfigureAwait(false);

            while (!stoppingToken.IsCancellationRequested)
            {
                await PurgerAsync(jours, stoppingToken).ConfigureAwait(false);
                await Task.Delay(Intervalle, stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Arrêt du service : rien à signaler.
        }
    }

    private async Task PurgerAsync(int jours, CancellationToken cancellationToken)
    {
        var limite = DateTimeOffset.UtcNow.AddDays(-jours);

        try
        {
            using var portee = scopes.CreateScope();
            var contexte = portee.ServiceProvider.GetRequiredService<TContext>();

            var supprimees = await contexte
                .Set<PersonalDataRead>()
                .Where(r => r.ReadAt < limite)
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);

            if (supprimees > 0)
            {
                // ON JOURNALISE CE QU'ON EFFACE. Une purge silencieuse est
                // indistinguable d'une purge qui ne tourne pas.
                journal.LogInformation(
                    "Journal des lectures : {Supprimees} traces de plus de {Jours} jours supprimees.",
                    supprimees,
                    jours);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // UNE PURGE QUI ECHOUE NE DOIT PAS TUER LE SERVICE. Elle
            // reessaiera demain ; ce qui compte est que l'echec se voie.
            journal.LogError(exception, "Journal des lectures : la purge a echoue.");
        }
    }
}

public static class PersonalDataReadPurgeExtensions
{
    /// <summary>
    /// Pose la purge du journal des lectures pour ce service. Sans réglage,
    /// elle se contente d'annoncer au démarrage qu'elle ne fera rien.
    /// </summary>
    public static IServiceCollection AddHbaPersonalDataReadPurge<TContext>(
        this IServiceCollection services,
        IConfiguration configuration)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<PersonalDataReadRetentionOptions>()
            .Bind(configuration.GetSection(PersonalDataReadRetentionOptions.SectionName));

        services.AddHostedService<PersonalDataReadPurge<TContext>>();

        return services;
    }
}
