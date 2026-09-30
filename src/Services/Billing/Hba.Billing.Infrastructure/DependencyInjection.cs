using Hba.BuildingBlocks.Application.Abstractions;
using Hba.BuildingBlocks.Application.Extensions;
using Hba.BuildingBlocks.Messaging.Extensions;
using Hba.BuildingBlocks.Messaging.Inbox;
using Hba.BuildingBlocks.Messaging.Outbox;
using Hba.BuildingBlocks.Persistence;
using Hba.Billing.Application.Common.Interfaces;
using Hba.Billing.Infrastructure.Persistence;
using Hba.Billing.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Hba.Billing.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddBillingInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddDbContext<BillingDbContext>(options =>
            options.UseNpgsql(
                configuration.Obligatoire("BillingDb"),
                npgsql => npgsql.MigrationsHistoryTable("__ef_migrations", BillingDbContext.Schema)));

        services.AddHbaAutoMigration<BillingDbContext>();
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<BillingDbContext>());

        services.AddSingleton(BillingDbContext.MessagingTables);
        services.AddScoped<IOutbox>(sp => new EfOutbox(sp.GetRequiredService<BillingDbContext>()));
        services.AddScoped<IOutboxStore>(sp => new EfOutboxStore(
            sp.GetRequiredService<BillingDbContext>(),
            BillingDbContext.MessagingTables));
        services.AddScoped<IInboxStore>(sp => new EfInboxStore(sp.GetRequiredService<BillingDbContext>()));
        services.AddScoped<IIdempotencyStore>(sp => new EfIdempotencyStore(sp.GetRequiredService<BillingDbContext>()));

        services.AddScoped<IBillingAccountRepository, BillingAccountRepository>();

        // LA TRANSACTION QUI FAIT TENIR LE VERROU. Voir ITransactionRunner : sans
        // elle, le « FOR UPDATE » du debit ne verrouillait rien, et deux courses
        // simultanees du meme donneur d'ordre passaient le plafond.
        services.AddScoped<ITransactionRunner, EfTransactionRunner>();

        // PRODUCTEUR KAFKA ET DISPATCHER D'OUTBOX. Sans cet appel, les messages
        // s'accumulaient dans la table d'Outbox et personne ne les publiait : le
        // mecanisme avait l'air en place, et l'alerte de solde ne partait pas.
        services.AddHbaMessaging(configuration, producerName: "billing", consumerGroupId: "billing");

        return services;
    }
}
