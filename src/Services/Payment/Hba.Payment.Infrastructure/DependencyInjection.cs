using System.Net.Http.Headers;
using Hba.BuildingBlocks.Application.Abstractions;
using Hba.BuildingBlocks.Application.Extensions;
using Hba.BuildingBlocks.Messaging.Extensions;
using Hba.BuildingBlocks.Messaging.Inbox;
using Hba.BuildingBlocks.Messaging.Outbox;
using Hba.BuildingBlocks.Persistence;
using Hba.Payment.Application.Common.Interfaces;
using Hba.Payment.Infrastructure.Persistence;
using Hba.Payment.Infrastructure.Persistence.Reads;
using Hba.Payment.Infrastructure.Persistence.Repositories;
using Hba.Payment.Infrastructure.Services.FedaPay;
using Hba.Payment.Infrastructure.Services.Loopback;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Hba.Payment.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Intentions de paiement et adaptateur FedaPay.
    /// </summary>
    public static IServiceCollection AddPaymentInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        services.AddDbContext<PaymentDbContext>(options =>
            options.UseNpgsql(
                configuration.GetConnectionString("PaymentDb"),
                npgsql => npgsql.MigrationsHistoryTable("__ef_migrations", PaymentDbContext.Schema)));

        services.AddHbaAutoMigration<PaymentDbContext>();
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<PaymentDbContext>());

        services.AddSingleton(PaymentDbContext.MessagingTables);
        services.AddScoped<IOutbox>(sp => new EfOutbox(sp.GetRequiredService<PaymentDbContext>()));
        services.AddScoped<IOutboxStore>(sp => new EfOutboxStore(
            sp.GetRequiredService<PaymentDbContext>(),
            PaymentDbContext.MessagingTables));
        services.AddScoped<IInboxStore>(sp => new EfInboxStore(sp.GetRequiredService<PaymentDbContext>()));
        services.AddScoped<IIdempotencyStore>(sp => new EfIdempotencyStore(sp.GetRequiredService<PaymentDbContext>()));

        services.AddScoped<IPaymentIntentRepository, PaymentIntentRepository>();
        services.AddScoped<IDriverLedgerRepository, DriverLedgerRepository>();
        services.AddScoped<IPayoutRequestRepository, PayoutRequestRepository>();

        // LES DEUX REGLAGES DU VERSEMENT, A ZERO PAR DEFAUT : le mecanisme
        // existe, les valeurs attendent une decision d'exploitation.
        services.AddOptions<PayoutOptions>()
            .Bind(configuration.GetSection(PayoutOptions.SectionName));

        // Lecture agregee : on ne charge pas les intentions pour les compter.
        services.AddScoped<IPaymentStatsReader, PaymentStatsReader>();
        services.AddScoped<IDriverStatementReader, DriverStatementReader>();
        services.AddHbaTime(configuration);

        AddProvider(services, configuration, environment);

        services.AddHbaMessaging(configuration, producerName: "payment", consumerGroupId: "payment");

        return services;
    }

    /// <summary>
    /// Choisit le fournisseur. « fedapay » par defaut ; « loopback » denoue
    /// les paiements tout seul et n'existe que pour le developpement.
    /// </summary>
    private static void AddProvider(
        IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var nom = configuration["Payment:Provider"] ?? "fedapay";

        if (!string.Equals(nom, "loopback", StringComparison.OrdinalIgnoreCase))
        {
            AddFedaPay(services, configuration, environment);
            return;
        }

        // REFUS EXPLICITE PLUTOT QUE DEGRADATION SILENCIEUSE, comme pour
        // Otp:FixedCodeForDevelopment et l'operateur SMS « log ». Un service
        // de paiement qui encaisse tout seul, en production, ferait partir des
        // courses que personne n'a payees — et rien dans les journaux ne
        // distinguerait ce cas d'un fonctionnement normal.
        if (!environment.IsDevelopment())
        {
            throw new InvalidOperationException(
                $"Payment:Provider vaut « loopback » en environnement {environment.EnvironmentName} : "
                + "les paiements seraient acceptes sans qu'aucun argent ne circule. Retirez ce reglage.");
        }

        services.AddOptions<LoopbackOptions>()
            .Bind(configuration.GetSection(LoopbackOptions.SectionName))
            .ValidateOnStart();

        // MEME INSTANCE DES DEUX COTES : le service d'arriere-plan lit la file
        // que le fournisseur remplit.
        services.AddSingleton<LoopbackPaymentProvider>();
        services.AddSingleton<IPaymentProvider>(sp => sp.GetRequiredService<LoopbackPaymentProvider>());
        services.AddHostedService<LoopbackSettlementService>();
    }

    private static void AddFedaPay(
        IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var builder = services.AddOptions<FedaPayOptions>()
            .Bind(configuration.GetSection(FedaPayOptions.SectionName));

        builder.Validate(
            options => options.WebhookToleranceSeconds is >= 30 and <= 3600,
            "FedaPay:WebhookToleranceSeconds doit tenir entre 30 et 3600 secondes.");

        // HORS DEVELOPPEMENT, UNE CLE MANQUANTE ARRETE LE SERVICE. La regle
        // apprise ailleurs dans ce depot s'applique telle quelle : une
        // configuration absente est definitive et doit empecher le demarrage,
        // tandis qu'une dependance momentanement injoignable se reessaie. Un
        // service de paiement sans cle ne guerira pas tout seul ; il accepterait
        // des commandes qu'il ne peut pas encaisser.
        if (!environment.IsDevelopment())
        {
            builder.Validate(
                options => options.IsConfigured,
                "FedaPay:SecretKey est absent. Le service de paiement ne peut pas demarrer sans.");

            builder.Validate(
                options => !string.IsNullOrWhiteSpace(options.WebhookSecret),
                "FedaPay:WebhookSecret est absent. Sans lui aucune notification de paiement "
                + "ne peut etre authentifiee, donc aucun paiement ne serait jamais confirme.");
        }

        builder.ValidateOnStart();

        services.AddHttpClient<IPaymentProvider, FedaPayClient>((provider, client) =>
        {
            var options = provider.GetRequiredService<IOptions<FedaPayOptions>>().Value;

            client.BaseAddress = options.BaseAddress;
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", options.SecretKey);

            // Equivalent du DeadlineInterceptor des appels gRPC. LA VALEUR
            // EST CONTRAINTE PAR L'AVAL : ouvrir un paiement fait DEUX appels
            // sortants, et Delivery nous accorde 20 secondes. Huit secondes
            // par appel laissent donc les deux tenir, avec de la marge pour
            // l'ecriture en base qui suit.
            client.Timeout = TimeSpan.FromSeconds(8);
        });

        services.AddSingleton<IWebhookVerifier, FedaPayWebhookVerifier>();
        services.AddHostedService<FedaPayStartupReport>();
    }
}
