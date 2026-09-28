using Hba.BuildingBlocks.Application.Abstractions;
using Hba.BuildingBlocks.Application.Extensions;
using Hba.BuildingBlocks.Grpc.Extensions;
using Hba.BuildingBlocks.Grpc.ServiceAuth;
using Hba.BuildingBlocks.Messaging.Extensions;
using Hba.BuildingBlocks.Messaging.Inbox;
using Hba.BuildingBlocks.Messaging.Outbox;
using Hba.BuildingBlocks.Persistence;
using Hba.Contracts.Driver.V1;
using Hba.Dispatch.Application.Common.Interfaces;
using Hba.Dispatch.Infrastructure.Persistence;
using Hba.Dispatch.Infrastructure.Persistence.Reads;
using Hba.Dispatch.Infrastructure.Persistence.Repositories;
using Hba.Dispatch.Infrastructure.Services.Drivers;
using Hba.Dispatch.Infrastructure.Services.Locking;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace Hba.Dispatch.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddDispatchInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddDbContext<DispatchDbContext>(options =>
            options.UseNpgsql(
                configuration.Obligatoire("DispatchDb"),
                npgsql => npgsql.MigrationsHistoryTable("__ef_migrations", DispatchDbContext.Schema)));

        services.AddHbaAutoMigration<DispatchDbContext>();
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<DispatchDbContext>());

        services.AddSingleton(DispatchDbContext.MessagingTables);
        services.AddScoped<IOutbox>(sp => new EfOutbox(sp.GetRequiredService<DispatchDbContext>()));
        services.AddScoped<IOutboxStore>(sp => new EfOutboxStore(
            sp.GetRequiredService<DispatchDbContext>(),
            DispatchDbContext.MessagingTables));
        services.AddScoped<IInboxStore>(sp => new EfInboxStore(sp.GetRequiredService<DispatchDbContext>()));
        services.AddScoped<IIdempotencyStore>(sp => new EfIdempotencyStore(sp.GetRequiredService<DispatchDbContext>()));

        services.AddScoped<IDispatchRepository, DispatchRepository>();

        services.AddOptions<DispatchOptions>()
            .Bind(configuration.GetSection(DispatchOptions.SectionName))
            .Validate(
                o => o.WaveRadiiMeters.Length > 0 && o.WaveRadiiMeters.All(r => r > 0),
                "Dispatch:WaveRadiiMeters doit contenir au moins un rayon strictement positif : sans vague, aucune course n'est proposee.")
            .Validate(
                o => o.OfferSeconds > 0 && o.DriversPerWave > 0,
                "Dispatch:OfferSeconds et Dispatch:DriversPerWave doivent etre strictement positifs.")
            .Validate(
                o => o.MaxDriversSolicited >= o.DriversPerWave,
                "Dispatch:MaxDriversSolicited doit valoir au moins DriversPerWave : un plafond plus bas que la "
                    + "largeur d'une vague arreterait la recherche avant la premiere offre.")
            .ValidateOnStart();

        AddRedis(services, configuration);

        // LE PORT GRPC DE DRIVER, PAS SON PORT REST. La confusion a deja coute
        // une soiree sur Delivery : 8080 est declare Http1, 8081 Http2, et un
        // appel gRPC vers le premier echoue sur HTTP_1_1_REQUIRED.
        services.AddHbaGrpcClient<DriverService.DriverServiceClient>(
            new Uri(configuration["Services:Driver"] ?? "http://driver:8081"),
            TimeSpan.FromSeconds(5));

        services.AddScoped<IDriverFinder, DriverFinder>();

        // Lecture agregee : une porte separee du depot.
        services.AddScoped<IDispatchStatsReader, DispatchStatsReader>();
        services.AddHbaTime(configuration);

        // LE PLANIFICATEUR N'A PAS D'UTILISATEUR : sans jeton de service,
        // Driver refuse la recherche et aucune offre ne part. Voir ADR 0018.
        services.AddHbaServiceToken(configuration);

        services.AddHbaMessaging(configuration, producerName: "dispatch", consumerGroupId: "dispatch");

        return services;
    }

    private static void AddRedis(IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Redis");

        // SANS VERROU, DEUX LIVREURS PEUVENT GAGNER LA MEME COURSE. Le jeton
        // de concurrence de la base rattraperait la plupart des cas, mais
        // « la plupart » n'est pas un niveau acceptable quand l'erreur envoie
        // deux personnes chercher le meme colis. Le service refuse de
        // demarrer.
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "ConnectionStrings:Redis est absent. Le verrou d'acceptation en depend : "
                + "sans lui deux livreurs pourraient accepter la meme course.");
        }

        services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(connectionString));
        services.AddScoped<IAcceptanceLock, RedisAcceptanceLock>();
    }
}
