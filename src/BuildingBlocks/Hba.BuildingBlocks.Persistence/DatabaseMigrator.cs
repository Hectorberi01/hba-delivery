using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Hba.BuildingBlocks.Persistence;

/// <summary>
/// Applique les migrations au démarrage quand Database:AutoMigrate vaut vrai.
///
/// C'est un compromis assumé, pas une bonne pratique universelle : avec
/// plusieurs instances, deux peuvent migrer en même temps. PostgreSQL protège
/// le DDL par des verrous et EF pose un verrou d'avis, donc la seconde attend ;
/// mais pour un déploiement sérieux, la migration devrait être une étape à part
/// avant le démarrage des conteneurs.
///
/// Tant que l'équipe tient dans une pièce, migrer au démarrage évite d'oublier
/// l'étape. Le jour où elle grandit, il suffit de repasser l'option à faux.
/// </summary>
public sealed class DatabaseMigrator<TContext>(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<DatabaseMigrator<TContext>> logger) : IHostedService
    where TContext : DbContext
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!configuration.GetValue("Database:AutoMigrate", defaultValue: false))
        {
            return;
        }

        using var scope = scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TContext>();

        var pending = await context.Database.GetPendingMigrationsAsync(cancellationToken).ConfigureAwait(false);
        var names = pending.ToList();

        if (names.Count == 0)
        {
            logger.LogInformation("Base à jour pour {Context}.", typeof(TContext).Name);
            return;
        }

        logger.LogWarning(
            "Application de {Count} migration(s) sur {Context} : {Migrations}.",
            names.Count,
            typeof(TContext).Name,
            string.Join(", ", names));

        await context.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

public static class DatabaseMigratorExtensions
{
    public static IServiceCollection AddHbaAutoMigration<TContext>(this IServiceCollection services)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);

        // Enregistré en premier pour passer avant les consommateurs Kafka et le
        // dispatcher d'Outbox, qui supposent des tables existantes.
        services.Insert(0, ServiceDescriptor.Singleton<IHostedService, DatabaseMigrator<TContext>>());

        return services;
    }
}
