using Hba.BuildingBlocks.Application.Abstractions;
using Hba.BuildingBlocks.Application.Extensions;
using Hba.BuildingBlocks.Grpc.Extensions;
using Hba.BuildingBlocks.Messaging.Extensions;
using Hba.BuildingBlocks.Messaging.Inbox;
using Hba.BuildingBlocks.Messaging.Outbox;
using Hba.BuildingBlocks.Persistence;
using Hba.Contracts.Routing.V1;
using Hba.Pricing.Application.Common.Interfaces;
using Hba.Pricing.Infrastructure.Persistence;
using Hba.Pricing.Infrastructure.Persistence.Repositories;
using Hba.Pricing.Infrastructure.Services.Routing;
using Hba.Pricing.Infrastructure.Services.Tariffs;
using Hba.Pricing.Infrastructure.Services.Zones;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Hba.Pricing.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Zones PostGIS, grilles tarifaires, devis. Seule autorite sur le prix.
    /// </summary>
    public static IServiceCollection AddPricingInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddDbContext<PricingDbContext>(options =>
            options.UseNpgsql(
                configuration.Obligatoire("PricingDb"),
                npgsql => npgsql.MigrationsHistoryTable("__ef_migrations", PricingDbContext.Schema)));

        services.AddHbaAutoMigration<PricingDbContext>();
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<PricingDbContext>());

        services.AddSingleton(PricingDbContext.MessagingTables);
        services.AddScoped<IOutbox>(sp => new EfOutbox(sp.GetRequiredService<PricingDbContext>()));
        services.AddScoped<IOutboxStore>(sp => new EfOutboxStore(
            sp.GetRequiredService<PricingDbContext>(),
            PricingDbContext.MessagingTables));
        services.AddScoped<IInboxStore>(sp => new EfInboxStore(sp.GetRequiredService<PricingDbContext>()));
        services.AddScoped<IIdempotencyStore>(sp => new EfIdempotencyStore(sp.GetRequiredService<PricingDbContext>()));

        services.AddScoped<ITariffRepository, TariffRepository>();
        services.AddScoped<IQuoteRepository, QuoteRepository>();
        services.AddScoped<IZoneRepository, ZoneRepository>();

        // LE CALENDRIER METIER, ET PRICING N'AGREGE RIEN.
        //
        // Les six autres services l'enregistrent pour grouper des courses par
        // journee locale ; Pricing, lui, n'a aucun tableau de bord. Il en a besoin
        // pour une seule question, posee a chaque devis : QUELLE HEURE EST-IL POUR
        // LE CHAUFFEUR ? C'est elle qui choisit la plage horaire de la grille, et
        // 22 h a Cotonou n'est pas 22 h UTC.
        //
        // IL MANQUAIT, ET RIEN NE LE DISAIT AVANT QUE LES PLAGES EXISTENT : aucun
        // code de ce service ne lisait l'heure locale, donc son absence ne cassait
        // rien. Le premier a la demander a fait echouer la construction du
        // conteneur — au demarrage, ce qui est la bonne facon de l'apprendre.
        services.AddHbaTime(configuration);

        services.AddOptions<ZoneOptions>()
            .Bind(configuration.GetSection(ZoneOptions.SectionName))
            .ValidateOnStart();
        // UN SEUL OBJET POUR LES DEUX PORTS, ET C'EST VOULU : chercher une zone et
        // ecrire son contour sont deux faces de la meme geometrie, sur la meme
        // connexion. Deux instances feraient deux endroits ou corriger le jour ou
        // ST_Covers deviendra autre chose.
        services.AddScoped<PostgisZoneStore>();
        services.AddScoped<IZoneLocator>(sp => sp.GetRequiredService<PostgisZoneStore>());
        services.AddScoped<IZoneRingStore>(sp => sp.GetRequiredService<PostgisZoneStore>());

        // LE MOTEUR DE ROUTAGE, ET SON REPLI LOCAL.
        //
        // HaversineRouteEngine n'est PAS supprime : il est retrograde. De moteur
        // unique — c'est-a-dire la source de TOUS les prix de la plateforme
        // jusqu'au 1er octobre 2026 — il devient le dernier recours, celui qui
        // repond quand le service Routing lui-meme est arrete.
        //
        // IL EST ENREGISTRE PAR SON TYPE CONCRET, pas par IRouteEngine : les
        // deux implementent la meme interface, et resoudre IRouteEngine doit
        // rendre le client de Routing, pas le repli.
        services.AddSingleton<HaversineRouteEngine>();

        // LE PORT GRPC DE ROUTING, PAS SON PORT REST. 8080 est declare Http1,
        // 8081 Http2 : un appel gRPC vers le premier echoue sur
        // HTTP_1_1_REQUIRED, et la confusion a deja coute une soiree ailleurs.
        //
        // TROIS SECONDES, ET NON CINQ. Un devis est demande pendant qu'un client
        // regarde son ecran ; Routing, lui, replie de son cote en cinq secondes
        // au plus. Attendre davantage ici ferait patienter le client pour une
        // reponse que l'autre service a deja renoncee a calculer.
        services.AddHbaGrpcClient<RoutingService.RoutingServiceClient>(
            new Uri(configuration["Services:Routing"] ?? "http://routing:8081"),
            TimeSpan.FromSeconds(3));

        services.AddScoped<IRouteEngine>(sp => new RoutingServiceRouteEngine(
            sp.GetRequiredService<RoutingService.RoutingServiceClient>(),
            sp.GetRequiredService<HaversineRouteEngine>(),
            sp.GetRequiredService<ILogger<RoutingServiceRouteEngine>>()));

        services.AddOptions<InitialTariffOptions>()
            .Bind(configuration.GetSection(InitialTariffOptions.SectionName))
            .ValidateOnStart();
        services.AddHostedService<InitialTariffSeeder>();

        services.AddHbaMessaging(configuration, producerName: "pricing", consumerGroupId: "pricing");

        return services;
    }
}
