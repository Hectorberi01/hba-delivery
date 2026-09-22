using Hba.BuildingBlocks.Application.Abstractions;
using Hba.BuildingBlocks.Grpc.Extensions;
using Hba.BuildingBlocks.Messaging.Extensions;
using Hba.BuildingBlocks.Messaging.Inbox;
using Hba.BuildingBlocks.Messaging.Outbox;
using Hba.BuildingBlocks.Persistence;
using Hba.Contracts.Driver.V1;
using Hba.Contracts.Payment.V1;
using Hba.Contracts.Pricing.V1;
using Hba.Delivery.Application.Ports;
using Hba.Delivery.Infrastructure.Clients;
using Hba.Delivery.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Hba.Delivery.Infrastructure.Extensions;

public static class DeliveryInfrastructureExtensions
{
    public static IServiceCollection AddDeliveryInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddDbContext<DeliveryDbContext>(options =>
            options.UseNpgsql(
                configuration.GetConnectionString("DeliveryDb"),
                npgsql => npgsql.MigrationsHistoryTable("__ef_migrations", DeliveryDbContext.Schema)));

        services.AddHbaAutoMigration<DeliveryDbContext>();
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<DeliveryDbContext>());
        services.AddScoped<IDeliveryRepository, DeliveryRepository>();
        services.AddSingleton<IReferenceGenerator, ReferenceGenerator>();

        // Outbox, Inbox et clés d'idempotence : implémentation commune à tous
        // les services, branchée sur le DbContext local.
        services.AddSingleton(DeliveryDbContext.MessagingTables);
        services.AddScoped<IOutbox>(sp => new EfOutbox(sp.GetRequiredService<DeliveryDbContext>()));
        services.AddScoped<IOutboxStore>(sp => new EfOutboxStore(
            sp.GetRequiredService<DeliveryDbContext>(),
            DeliveryDbContext.MessagingTables));
        services.AddScoped<IInboxStore>(sp => new EfInboxStore(sp.GetRequiredService<DeliveryDbContext>()));
        services.AddScoped<IIdempotencyStore>(sp => new EfIdempotencyStore(sp.GetRequiredService<DeliveryDbContext>()));

        services.AddHbaMessaging(configuration, producerName: "delivery", consumerGroupId: "delivery");

        AddInternalClients(services, configuration);

        return services;
    }

    private static void AddInternalClients(IServiceCollection services, IConfiguration configuration)
    {
        var pricing = new Uri(configuration["Services:Pricing"] ?? "http://pricing:8080");
        var payment = new Uri(configuration["Services:Payment"] ?? "http://payment:8080");
        var driver = new Uri(configuration["Services:Driver"] ?? "http://driver:8080");

        services.AddHbaGrpcClient<PricingService.PricingServiceClient>(pricing, TimeSpan.FromSeconds(3));
        services.AddHbaGrpcClient<PaymentService.PaymentServiceClient>(payment, TimeSpan.FromSeconds(5));
        services.AddHbaGrpcClient<DriverService.DriverServiceClient>(driver, TimeSpan.FromSeconds(3));

        services.AddScoped<IPricingClient, PricingGrpcClient>();
        services.AddScoped<IPaymentClient, PaymentGrpcClient>();
        services.AddScoped<IDriverDirectory, DriverGrpcDirectory>();
    }
}
