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
using Hba.Ride.Application.Ports;
using Hba.Ride.Infrastructure.Clients;
using Hba.Ride.Infrastructure.Persistence;
using Hba.Ride.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Hba.Ride.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Les courses en base, le prix chez Pricing, les faits dans l'Outbox.
    /// </summary>
    ///
    /// <remarks>
    /// NI REDIS NI STOCKAGE D'OBJETS, CONTRAIREMENT À DRIVER. Une course ne
    /// porte aucune position temps réel — elles restent chez Driver — et aucune
    /// pièce jointe. Déclarer ces dépendances « au cas où » ferait échouer le
    /// démarrage sur une configuration que le service n'utilise pas.
    /// </remarks>
    public static IServiceCollection AddRideInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddDbContext<RideDbContext>(options =>
            options.UseNpgsql(
                configuration.Obligatoire("RideDb"),
                npgsql => npgsql.MigrationsHistoryTable("__ef_migrations", RideDbContext.Schema)));

        services.AddHbaAutoMigration<RideDbContext>();
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<RideDbContext>());

        services.AddSingleton(RideDbContext.MessagingTables);
        services.AddScoped<IOutbox>(sp => new EfOutbox(sp.GetRequiredService<RideDbContext>()));
        services.AddScoped<IOutboxStore>(sp => new EfOutboxStore(
            sp.GetRequiredService<RideDbContext>(),
            RideDbContext.MessagingTables));
        services.AddScoped<IInboxStore>(sp => new EfInboxStore(sp.GetRequiredService<RideDbContext>()));
        services.AddScoped<IIdempotencyStore>(sp => new EfIdempotencyStore(sp.GetRequiredService<RideDbContext>()));

        services.AddScoped<IRideRepository, RideRepository>();

        // LE PORT 8081, PAS 8080. Chaque service ouvre deux points d'écoute :
        // 8080 en HTTP/1 pour le REST, 8081 en HTTP/2 pour le gRPC. Un appel
        // gRPC vers 8080 ne tombe pas en délai dépassé — Kestrel refuse la
        // négociation et répond HTTP_1_1_REQUIRED, que le client .NET présente
        // comme « Error starting gRPC call ».
        services.AddHbaGrpcClient<PricingService.PricingServiceClient>(
            new Uri(configuration["Services:Pricing"] ?? "http://pricing:8081"),
            TimeSpan.FromSeconds(3));

        services.AddScoped<IPricingClient, PricingGrpcClient>();

        // PAYMENT EST LE SEUL A SORTIR DU RESEAU, et l'echeance le dit : ouvrir un
        // paiement demande deux appels a l'agregateur — creation de la transaction,
        // puis jeton — sur un lien qui traverse l'Atlantique. Delivery a appris a
        // ses depens qu'une echeance courte coupe l'appel pendant que le fournisseur
        // repond encore, et laisse une transaction ouverte que personne ne reclame.
        services.AddHbaGrpcClient<PaymentService.PaymentServiceClient>(
            new Uri(configuration["Services:Payment"] ?? "http://payment:8081"),
            TimeSpan.FromSeconds(20));

        services.AddScoped<IRidePaymentClient, PaymentGrpcClient>();

        // DRIVER, POUR LE PROFIL DU CHAUFFEUR AFFECTE. L'appel a lieu une fois par
        // course, a l'affectation, et non a chaque lecture : la passerelle le faisait
        // a chaque rafraichissement de l'ecran de suivi.
        services.AddHbaGrpcClient<DriverService.DriverServiceClient>(
            new Uri(configuration["Services:Driver"] ?? "http://driver:8081"),
            TimeSpan.FromSeconds(3));

        services.AddScoped<IRideDriverDirectory, DriverGrpcDirectory>();

        // LE JETON DE SERVICE : l'appel vers Driver descend d'un consommateur Kafka,
        // sans utilisateur ni jeton a reporter (ADR 0018).
        services.AddHbaServiceToken(configuration);

        services.AddHbaTime(configuration);
        services.AddHbaMessaging(configuration, producerName: "ride", consumerGroupId: "ride");

        return services;
    }
}
