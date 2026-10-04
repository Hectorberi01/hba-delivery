using Hba.BuildingBlocks.Application.Abstractions;
using Hba.BuildingBlocks.Application.Extensions;
using Hba.BuildingBlocks.Messaging.Extensions;
using Hba.BuildingBlocks.Messaging.Inbox;
using Hba.BuildingBlocks.Messaging.Outbox;
using Hba.BuildingBlocks.Persistence;
using Hba.BuildingBlocks.Storage;
using Hba.Driver.Application.Common.Interfaces;
using Hba.Driver.Infrastructure.Persistence;
using Hba.Driver.Infrastructure.Persistence.Reads;
using Hba.Driver.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Hba.Driver.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Dossiers des livreurs en base. LES POSITIONS SONT DANS LE SERVICE LOCATION
    /// depuis le 1er octobre 2026 : ce service ne sait plus ou se trouvent les gens.
    /// </summary>
    public static IServiceCollection AddDriverInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddDbContext<DriverDbContext>(options =>
        {
            options.UseNpgsql(
                configuration.Obligatoire("DriverDb"),
                npgsql => npgsql.MigrationsHistoryTable("__ef_migrations", DriverDbContext.Schema));

            // LES VALEURS DES PARAMETRES DANS LES JOURNAUX, SUR DEMANDE
            // SEULE. Sans elles, une instruction journalisee montre « @p0 »
            // la ou on cherche precisement quelle valeur de xmin a ete
            // comparee. Avec elles, des donnees reelles se retrouvent dans
            // les journaux : le drapeau reste faux partout sauf le temps
            // d'un diagnostic.
            if (configuration.GetValue<bool>("Diagnostics:SqlParameters"))
            {
                options.EnableSensitiveDataLogging();
            }
        });

        services.AddHbaAutoMigration<DriverDbContext>();
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<DriverDbContext>());

        services.AddSingleton(DriverDbContext.MessagingTables);
        services.AddScoped<IOutbox>(sp => new EfOutbox(sp.GetRequiredService<DriverDbContext>()));
        services.AddScoped<IOutboxStore>(sp => new EfOutboxStore(
            sp.GetRequiredService<DriverDbContext>(),
            DriverDbContext.MessagingTables));
        services.AddScoped<IInboxStore>(sp => new EfInboxStore(sp.GetRequiredService<DriverDbContext>()));
        services.AddScoped<IIdempotencyStore>(sp => new EfIdempotencyStore(sp.GetRequiredService<DriverDbContext>()));

        // JOURNAL DES LECTURES. Le dossier KYC porte des pieces d'identite :
        // c'est la lecture la plus sensible du service.
        services.AddScoped<IPersonalDataReadLog>(sp => new EfPersonalDataReadLog(
            sp.GetRequiredService<DriverDbContext>(),
            sp.GetRequiredService<ICallerContext>(),
            sp.GetRequiredService<ILogger<EfPersonalDataReadLog>>()));

        // SA PURGE, ETEINTE TANT QUE RIEN N'EST REGLE. La duree de
        // conservation d'une trace d'acces est une decision juridique ; le
        // code pose le mecanisme et l'annonce au demarrage, il ne choisit pas
        // le nombre de mois.
        services.AddHbaPersonalDataReadPurge<DriverDbContext>(configuration);

        services.AddScoped<IDriverRepository, DriverRepository>();

        // LE CHEMIN DU .ENV EST PASSE AU SOCLE, parce que c'est lui que cite
        // le message d'arret quand les cles manquent. Un message partage qui
        // dirait « le .env du service » laisserait deviner lequel.
        services.AddHbaObjectStore(configuration, "src/Services/Driver/.env");

        // Lecture agregee : une porte separee du depot.
        services.AddScoped<IDriverStatsReader, DriverStatsReader>();
        services.AddHbaTime(configuration);

        services.AddHbaMessaging(configuration, producerName: "driver", consumerGroupId: "driver");

        return services;
    }

    // ---- LES POSITIONS ONT QUITTE CE SERVICE ---------------------------------
    //
    // AddLocations vivait ici jusqu'au 1er octobre 2026 : elle exigeait Redis au
    // demarrage, posait DriverLocationOptions et enregistrait RedisDriverLocationStore.
    // Tout est parti dans le service Location.
    //
    // DRIVER N'A PLUS AUCUNE DEPENDANCE A REDIS, et c'est verifiable d'un coup d'oeil :
    // plus un seul « using StackExchange.Redis » dans ce projet. Si l'un revient, c'est
    // que les positions sont en train de rentrer par la fenetre.
}
