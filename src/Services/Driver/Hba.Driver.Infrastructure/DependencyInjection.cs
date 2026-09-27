using Hba.BuildingBlocks.Application.Abstractions;
using Hba.BuildingBlocks.Application.Extensions;
using Hba.BuildingBlocks.Messaging.Extensions;
using Hba.BuildingBlocks.Messaging.Inbox;
using Hba.BuildingBlocks.Messaging.Outbox;
using Hba.BuildingBlocks.Persistence;
using Hba.Driver.Application.Common.Interfaces;
using Hba.Driver.Infrastructure.Persistence;
using Hba.Driver.Infrastructure.Persistence.Reads;
using Hba.Driver.Infrastructure.Persistence.Repositories;
using Hba.Driver.Infrastructure.Services.Locations;
using Hba.Driver.Infrastructure.Services.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Minio;
using StackExchange.Redis;

namespace Hba.Driver.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Dossiers des livreurs en base, positions dans Redis GEO.
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
                configuration.GetConnectionString("DriverDb"),
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

        AddLocations(services, configuration);
        AddObjectStore(services, configuration);

        // Lecture agregee : une porte separee du depot.
        services.AddScoped<IDriverStatsReader, DriverStatsReader>();
        services.AddHbaTime(configuration);

        services.AddHbaMessaging(configuration, producerName: "driver", consumerGroupId: "driver");

        return services;
    }

    private static void AddLocations(IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Redis");

        // SANS REDIS, AUCUNE POSITION, DONC AUCUNE OFFRE. Le service
        // demarrerait, repondrait a /health, accepterait les passages en
        // ligne — et aucune vague ne trouverait jamais personne. C'est
        // exactement la panne invisible que ce depot a deja rencontree trois
        // fois ; elle s'arrete ici.
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "ConnectionStrings:Redis est absent. Les positions des livreurs y vivent : "
                + "sans lui le dispatch ne trouverait jamais personne.");
        }

        services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(connectionString));

        services.AddOptions<DriverLocationOptions>()
            .Bind(configuration.GetSection(DriverLocationOptions.SectionName))
            .ValidateOnStart();

        services.AddScoped<IDriverLocationStore, RedisDriverLocationStore>();
    }

    /// <summary>
    /// Stockage des pièces du dossier (ADR 0021).
    ///
    /// ValidateDataAnnotations + ValidateOnStart : un bucket ou une clé
    /// absente fait REFUSER LE DÉMARRAGE. Sans cela, le service accepterait
    /// des pièces d'identité et les perdrait une par une, en ne laissant
    /// qu'une ligne d'avertissement dans les journaux.
    /// </summary>
    private static void AddObjectStore(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<ObjectStoreOptions>()
            .Bind(configuration.GetSection(ObjectStoreOptions.SectionName))
            .ValidateDataAnnotations()

            // « The AccessKey field is required » EST EXACT ET INUTILE. Il dit
            // ce qui manque, jamais où le prendre — et ces deux clés ne se
            // devinent pas : elles n'existent qu'après « make garage-init »,
            // et Garage n'affiche le secret qu'une seule fois. Un message
            // d'arrêt qui n'indique pas le geste suivant fait perdre le
            // quart d'heure qu'il prétend faire gagner.
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.AccessKey)
                           && !string.IsNullOrWhiteSpace(options.SecretKey),
                "Les clés du stockage objet sont absentes. Le service refuse de "
                + "démarrer plutôt que d'accepter des pièces d'identité et de les "
                + "perdre. Lancez « make garage-init » : il crée la clé dans Garage "
                + "et renseigne OBJECTSTORE_ACCESS_KEY et OBJECTSTORE_SECRET_KEY "
                + "dans src/Services/Driver/.env.")
            .ValidateOnStart();

        // Le client est sans état et coûte une poignée de connexions : un
        // seul suffit pour tout le service.
        services.AddSingleton<IMinioClient>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<ObjectStoreOptions>>().Value;

            return new MinioClient()
                .WithEndpoint(options.Endpoint)
                .WithCredentials(options.AccessKey, options.SecretKey)
                .WithRegion(options.Region)
                .WithSSL(options.UseSsl)
                .Build();
        });

        // UN SECOND CLIENT QUI NE PARLE A PERSONNE. Celui-ci ne sert qu'à
        // SIGNER : la signature d'une URL présignée est un calcul local, sans
        // appel réseau. Il peut donc pointer vers une adresse que le service
        // lui-même ne joindrait pas — et c'est exactement le but, puisque
        // l'adresse qu'il faut signer est celle du navigateur et du
        // téléphone, pas celle du réseau Docker.
        services.AddKeyedSingleton<IMinioClient>(ObjectStoreOptions.PresigningClientKey, (sp, _) =>
        {
            var options = sp.GetRequiredService<IOptions<ObjectStoreOptions>>().Value;

            return new MinioClient()
                .WithEndpoint(options.SigningEndpoint)
                .WithCredentials(options.AccessKey, options.SecretKey)
                .WithRegion(options.Region)
                .WithSSL(options.SigningUseSsl)
                .Build();
        });

        services.AddScoped<IObjectStore, S3ObjectStore>();
    }
}
