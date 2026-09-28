using System.Net.Http.Headers;
using Hba.BuildingBlocks.Application.Abstractions;
using Hba.BuildingBlocks.Messaging.Extensions;
using Hba.BuildingBlocks.Messaging.Inbox;
using Hba.BuildingBlocks.Messaging.Outbox;
using Hba.BuildingBlocks.Persistence;
using Hba.Notification.Application.Ports;
using Hba.Notification.Domain.Messages;
using Hba.Notification.Infrastructure.Persistence;
using Hba.Notification.Infrastructure.Senders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Hba.Notification.Infrastructure.Extensions;

public static class NotificationInfrastructureExtensions
{
    public static IServiceCollection AddNotificationInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        services.AddDbContext<NotificationDbContext>(options =>
            options.UseNpgsql(
                configuration.Obligatoire("NotificationDb"),
                npgsql => npgsql.MigrationsHistoryTable("__ef_migrations", NotificationDbContext.Schema)));

        services.AddHbaAutoMigration<NotificationDbContext>();
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<NotificationDbContext>());
        services.AddScoped<ISentNotificationRepository, SentNotificationRepository>();

        services.AddSingleton(NotificationDbContext.MessagingTables);
        services.AddScoped<IOutbox>(sp => new EfOutbox(sp.GetRequiredService<NotificationDbContext>()));
        services.AddScoped<IOutboxStore>(sp => new EfOutboxStore(
            sp.GetRequiredService<NotificationDbContext>(),
            NotificationDbContext.MessagingTables));
        services.AddScoped<IInboxStore>(sp => new EfInboxStore(sp.GetRequiredService<NotificationDbContext>()));
        services.AddScoped<IIdempotencyStore>(sp => new EfIdempotencyStore(sp.GetRequiredService<NotificationDbContext>()));

        services.AddHbaMessaging(configuration, producerName: "notification", consumerGroupId: "notification");

        AddSenders(services, configuration, environment);

        return services;
    }

    private static void AddSenders(
        IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        services.AddOptions<SmsOptions>()
            .Bind(configuration.GetSection(SmsOptions.SectionName))
            .ValidateOnStart();

        var provider = configuration[$"{SmsOptions.SectionName}:Provider"] ?? "none";

        if (string.Equals(provider, "ovh", StringComparison.OrdinalIgnoreCase))
        {
            AddOvh(services, configuration);
        }
        else if (string.Equals(provider, "log", StringComparison.OrdinalIgnoreCase) && environment.IsDevelopment())
        {
            services.AddSingleton<INotificationSender, LoggingSmsSender>();
        }
        else
        {
            if (string.Equals(provider, "log", StringComparison.OrdinalIgnoreCase))
            {
                // Refus explicite plutôt que dégradation silencieuse : écrire
                // les codes de connexion dans les journaux de production serait
                // une fuite, pas une commodité.
                services.AddSingleton<INotificationSender>(sp =>
                {
                    sp.GetRequiredService<ILogger<UnconfiguredSender>>().LogError(
                        "Sms:Provider vaut « log » hors développement : le fournisseur est désactivé.");

                    return new UnconfiguredSender(NotificationChannel.Sms);
                });
            }
            else
            {
                // IL FAUT DIRE QUELLE VALEUR A ETE LUE, PAS SEULEMENT QU'IL N'Y
                // EN A PAS. Une faute de frappe dans SMS_PROVIDER, ou un
                // conteneur cree avant que le .env soit rempli, donnent tous
                // deux un service qui demarre sans broncher et refuse chaque
                // message trois fois, des heures plus tard, avec un « Aucun
                // fournisseur configure » qui ne dit pas pourquoi. La valeur
                // effectivement lue est la seule information qui tranche.
                services.AddSingleton<INotificationSender>(sp =>
                {
                    sp.GetRequiredService<ILogger<UnconfiguredSender>>().LogError(
                        "Sms:Provider vaut « {Provider} » : aucun operateur SMS n'est enregistre. "
                        + "Attendu « ovh », ou « log » en developpement. Les codes de connexion et "
                        + "de remise ne partiront pas. Si le .env est correct, le conteneur a ete "
                        + "cree avant : docker compose up -d --force-recreate.",
                        provider);

                    return new UnconfiguredSender(NotificationChannel.Sms);
                });
            }
        }

        AddWhatsApp(services, configuration);

        // Le push n'a pas de fournisseur : il est déclaré pour que le journal
        // distingue « pas de fournisseur » d'« erreur d'envoi ».
        services.AddSingleton<INotificationSender>(_ => new UnconfiguredSender(NotificationChannel.Push));

        // Etat des canaux au demarrage, pas au premier message.
        services.AddHostedService<SenderStartupReport>();
    }

    /// <summary>
    /// Opérateur SMS. Le SMS reste le SEUL canal vers le destinataire d'un
    /// colis, qui n'a pas de compte et ne peut donc pas donner d'opt-in
    /// WhatsApp — et ce code de remise est la preuve de livraison (ADR 0005).
    /// Sans opérateur, aucune livraison ne peut être clôturée.
    /// </summary>
    private static void AddOvh(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<OvhSmsOptions>()
            .Bind(configuration.GetSection(OvhSmsOptions.SectionName))
            .ValidateOnStart();

        services.AddHttpClient<INotificationSender, OvhSmsSender>()
            .ConfigureHttpClient(client =>
            {
                // Un code qui met plus de dix secondes a partir n'a plus
                // d'interet : mieux vaut echouer net.
                client.Timeout = TimeSpan.FromSeconds(10);
            });
    }

    /// <summary>
    /// WhatsApp porte le code de connexion (ADR 0014). L'adaptateur n'est
    /// enregistré que s'il est réellement configuré ; sinon le canal existe
    /// quand même, mais dit qu'il n'a pas de fournisseur — ce qui fait basculer
    /// la chaîne sur le SMS au lieu de laisser croire à un envoi réussi.
    /// </summary>
    private static void AddWhatsApp(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<WhatsAppOptions>()
            .Bind(configuration.GetSection(WhatsAppOptions.SectionName))
            .ValidateOnStart();

        var provider = configuration[$"{WhatsAppOptions.SectionName}:Provider"] ?? "none";

        if (!string.Equals(provider, "cloud", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<INotificationSender>(_ => new UnconfiguredSender(NotificationChannel.WhatsApp));
            return;
        }

        services.AddHttpClient<INotificationSender, WhatsAppCloudSender>()
            .ConfigureHttpClient((sp, client) =>
            {
                var options = sp.GetRequiredService<IOptions<WhatsAppOptions>>().Value;

                client.BaseAddress = new Uri("https://graph.facebook.com/");
                client.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", options.AccessToken);

                // Un code de connexion qui met plus de dix secondes à partir
                // n'a plus d'intérêt : mieux vaut basculer sur le SMS.
                client.Timeout = TimeSpan.FromSeconds(10);
            });
    }
}
