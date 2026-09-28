using Hba.BuildingBlocks.Application.Abstractions;
using Hba.BuildingBlocks.Application.Extensions;
using Hba.BuildingBlocks.Grpc.Extensions;
using Hba.BuildingBlocks.Grpc.ServiceAuth;
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
using Hba.Delivery.Infrastructure.Persistence.Reads;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

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
                configuration.Obligatoire("DeliveryDb"),
                npgsql => npgsql.MigrationsHistoryTable("__ef_migrations", DeliveryDbContext.Schema)));

        services.AddHbaAutoMigration<DeliveryDbContext>();
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<DeliveryDbContext>());
        services.AddScoped<IDeliveryRepository, DeliveryRepository>();

        // Lecture agregee : une porte separee du depot. On ne charge pas cent
        // mille agregats pour en tirer six nombres.
        services.AddScoped<IDeliveryStatsReader, DeliveryStatsReader>();
        services.AddHbaTime(configuration);
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

        // JOURNAL DES LECTURES DE DONNEES PERSONNELLES. Il ecrit dans la base
        // de CE service : la regle « une base par service » vaut aussi pour
        // un journal d'audit, et la fiche client comme le cumul facture se
        // lisent chacun chez soi.
        services.AddScoped<IPersonalDataReadLog>(sp => new EfPersonalDataReadLog(
            sp.GetRequiredService<DeliveryDbContext>(),
            sp.GetRequiredService<ICallerContext>(),
            sp.GetRequiredService<ILogger<EfPersonalDataReadLog>>()));

        // SA PURGE, ETEINTE TANT QUE RIEN N'EST REGLE. La duree de
        // conservation d'une trace d'acces est une decision juridique ; le
        // code pose le mecanisme et l'annonce au demarrage, il ne choisit pas
        // le nombre de mois.
        services.AddHbaPersonalDataReadPurge<DeliveryDbContext>(configuration);

        services.AddHbaMessaging(configuration, producerName: "delivery", consumerGroupId: "delivery");

        AddInternalClients(services, configuration);

        return services;
    }

    private static void AddInternalClients(IServiceCollection services, IConfiguration configuration)
    {
        // LE PORT 8081, PAS 8080. Chaque service ouvre DEUX points d'ecoute
        // (voir la section Kestrel de son appsettings.json) : 8080 est declare
        // « Protocols: Http1 » et porte le REST, 8081 est declare
        // « Protocols: Http2 » et porte le gRPC. Un appel gRPC vers 8080 ne
        // tombe pas en timeout et ne remonte pas d'erreur metier : Kestrel
        // refuse la negociation et renvoie le code HTTP/2 HTTP_1_1_REQUIRED
        // (0xd), que le client .NET presente comme « Error starting gRPC call ».
        // En developpement les ports sont 500x pour le REST et 600x pour le gRPC.
        var pricing = new Uri(configuration["Services:Pricing"] ?? "http://pricing:8081");
        var payment = new Uri(configuration["Services:Payment"] ?? "http://payment:8081");
        var driver = new Uri(configuration["Services:Driver"] ?? "http://driver:8081");

        services.AddHbaGrpcClient<PricingService.PricingServiceClient>(pricing, TimeSpan.FromSeconds(3));

        // PAYMENT EST LE SEUL A SORTIR DU RESEAU. Ouvrir un paiement demande
        // deux appels a l'agregateur — creation de la transaction, puis jeton —
        // sur un lien qui traverse l'Atlantique. Les 5 secondes d'origine
        // coupaient l'appel pendant que le fournisseur repondait encore, et
        // laissaient une transaction ouverte que personne ne reclamait ensuite.
        services.AddHbaGrpcClient<PaymentService.PaymentServiceClient>(payment, TimeSpan.FromSeconds(20));
        services.AddHbaGrpcClient<DriverService.DriverServiceClient>(driver, TimeSpan.FromSeconds(3));

        services.AddScoped<IPricingClient, PricingGrpcClient>();
        services.AddScoped<IPaymentClient, PaymentGrpcClient>();
        services.AddScoped<IDriverDirectory, DriverGrpcDirectory>();

        // L'AFFECTATION DU LIVREUR NAIT D'UN EVENEMENT, PAS D'UNE REQUETE :
        // il faut une identite propre pour appeler Driver. Voir ADR 0018.
        services.AddHbaServiceToken(configuration);
    }
}
