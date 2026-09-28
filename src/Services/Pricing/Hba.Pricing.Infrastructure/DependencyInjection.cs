using Hba.BuildingBlocks.Application.Abstractions;
using Hba.BuildingBlocks.Messaging.Extensions;
using Hba.BuildingBlocks.Messaging.Inbox;
using Hba.BuildingBlocks.Messaging.Outbox;
using Hba.BuildingBlocks.Persistence;
using Hba.Pricing.Application.Common.Interfaces;
using Hba.Pricing.Infrastructure.Persistence;
using Hba.Pricing.Infrastructure.Persistence.Repositories;
using Hba.Pricing.Infrastructure.Services.Routing;
using Hba.Pricing.Infrastructure.Services.Tariffs;
using Hba.Pricing.Infrastructure.Services.Zones;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

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

        services.AddOptions<ZoneOptions>()
            .Bind(configuration.GetSection(ZoneOptions.SectionName))
            .ValidateOnStart();
        services.AddScoped<IZoneLocator, ConfiguredZoneLocator>();

        // Adaptateur provisoire : voir le commentaire de la classe, qui dit
        // pourquoi il contredit le contrat et comment il se remplace.
        services.AddSingleton<IRouteEngine, HaversineRouteEngine>();

        services.AddOptions<InitialTariffOptions>()
            .Bind(configuration.GetSection(InitialTariffOptions.SectionName))
            .ValidateOnStart();
        services.AddHostedService<InitialTariffSeeder>();

        services.AddHbaMessaging(configuration, producerName: "pricing", consumerGroupId: "pricing");

        return services;
    }
}
