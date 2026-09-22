using Hba.BuildingBlocks.Application.Abstractions;
using Hba.BuildingBlocks.Messaging.Extensions;
using Hba.BuildingBlocks.Messaging.Inbox;
using Hba.BuildingBlocks.Messaging.Outbox;
using Hba.BuildingBlocks.Persistence;
using Hba.Directory.Application.Ports;
using Hba.Directory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Hba.Directory.Infrastructure.Extensions;

public static class DirectoryInfrastructureExtensions
{
    public static IServiceCollection AddDirectoryInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddDbContext<DirectoryDbContext>(options =>
            options.UseNpgsql(
                configuration.GetConnectionString("DirectoryDb"),
                npgsql => npgsql.MigrationsHistoryTable("__ef_migrations", DirectoryDbContext.Schema)));

        services.AddHbaAutoMigration<DirectoryDbContext>();
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<DirectoryDbContext>());
        services.AddScoped<ICustomerRepository, CustomerRepository>();
        services.AddScoped<IMerchantRepository, MerchantRepository>();

        services.AddSingleton(DirectoryDbContext.MessagingTables);
        services.AddScoped<IOutbox>(sp => new EfOutbox(sp.GetRequiredService<DirectoryDbContext>()));
        services.AddScoped<IOutboxStore>(sp => new EfOutboxStore(
            sp.GetRequiredService<DirectoryDbContext>(),
            DirectoryDbContext.MessagingTables));
        services.AddScoped<IInboxStore>(sp => new EfInboxStore(sp.GetRequiredService<DirectoryDbContext>()));
        services.AddScoped<IIdempotencyStore>(sp => new EfIdempotencyStore(sp.GetRequiredService<DirectoryDbContext>()));

        services.AddHbaMessaging(configuration, producerName: "directory", consumerGroupId: "directory");

        return services;
    }
}
