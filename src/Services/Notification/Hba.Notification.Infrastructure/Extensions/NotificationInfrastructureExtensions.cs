using System.Net.Http.Headers;
using Hba.BuildingBlocks.Application.Abstractions;
using Hba.BuildingBlocks.Grpc.ServiceAuth;
using Hba.BuildingBlocks.Grpc.Extensions;
using Hba.BuildingBlocks.Messaging.Extensions;
using Hba.BuildingBlocks.Messaging.Inbox;
using Hba.BuildingBlocks.Messaging.Outbox;
using Hba.BuildingBlocks.Persistence;
using Hba.Contracts.Directory.V1;
using Hba.Notification.Application.Ports;
using Hba.Notification.Domain.Messages;
using Hba.Notification.Infrastructure.Annuaire;
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

        // LE CARNET D'ADRESSES : Directory, interroge au moment d'envoyer.
        //
        // C'est le seul appel sortant de Notification vers un autre service, et
        // il n'existe que pour le courriel : un SMS part vers un numero que
        // l'appelant connait, un recu de course part vers une adresse que seul
        // l'annuaire detient. Voir SendEmail dans le contrat, qui dit pourquoi
        // ce n'est pas Delivery qui la cherche.
        services.AddHbaGrpcClient<DirectoryService.DirectoryServiceClient>(
            new Uri(configuration["Services:Directory"] ?? "http://directory:8081"));

        // LE JETON DE SERVICE, SANS LEQUEL CET APPEL NE PASSAIT PAS.
        //
        // L'appel a Directory descend d'un consommateur Kafka : pas de
        // HttpContext, donc rien a reporter, donc un appel nu refuse par un
        // service en [Authorize]. L'echec etait avale en avertissement et le
        // recu consigne « le compte n'a pas de courriel » : aucun recu ne
        // partait, et le journal accusait le client.
        //
        // ServiceToken:ClientId et ClientSecret sont deja dans le compose de ce
        // service ; c'est l'enregistrement qui manquait.
        services.AddHbaServiceToken(configuration);

        services.AddScoped<ICarnetDAdresses, CarnetDAdresses>();

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

        AddEmail(services, configuration);

        // Etat des canaux au demarrage, pas au premier message.
        services.AddHostedService<SenderStartupReport>();
    }

    /// <summary>
    /// Le courriel : Resend, ou un relais SMTP.
    /// </summary>
    ///
    /// <remarks>
    /// MEME FORME QUE LE SMS, ET POUR LA MEME RAISON. Un canal, une clé
    /// « Provider », un adaptateur par fournisseur derrière le même port. Ce
    /// qui change d'un fournisseur à l'autre ne touche que ce fichier.
    ///
    /// RESEND PAR DEFAUT, SMTP EN ISSUE DE SECOURS. Resend rend l'identifiant
    /// du message, honore la clé d'idempotence et nomme ses refus — un domaine
    /// non vérifié y est un 403 explicite, là où le même refus arrive en
    /// « 550 » opaque par SMTP. Le relais SMTP reste parce qu'il marche avec
    /// n'importe quel fournisseur, y compris Resend lui-même, le jour où il
    /// faudra sortir vite.
    ///
    /// L'EXPEDITEUR FAIT PARTIE DE LA CONFIGURATION, pas seulement la clé. Un
    /// fournisseur joignable sans adresse d'expédition n'envoie rien, et il
    /// vaut mieux le dire au démarrage qu'au premier reçu.
    /// </remarks>
    private static void AddEmail(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<ResendOptions>()
            .Bind(configuration.GetSection(ResendOptions.SectionName))
            .ValidateOnStart();

        services.AddOptions<SmtpOptions>()
            .Bind(configuration.GetSection(SmtpOptions.SectionName))
            .ValidateOnStart();

        var fournisseur = configuration["Email:Provider"] ?? "resend";

        if (string.Equals(fournisseur, "resend", StringComparison.OrdinalIgnoreCase))
        {
            var cle = configuration[$"{ResendOptions.SectionName}:ApiKey"];
            var expediteur = configuration[$"{ResendOptions.SectionName}:From"];

            if (!string.IsNullOrWhiteSpace(cle) && !string.IsNullOrWhiteSpace(expediteur))
            {
                // L'ADRESSE DE BASE ET LE JETON SONT POSES ICI, UNE FOIS. Les
                // mettre dans l'adaptateur les ferait relire a chaque envoi, et
                // ferait porter a une classe de domaine applicatif la
                // connaissance d'une URL.
                services.AddHttpClient<INotificationSender, ResendEmailSender>((sp, client) =>
                {
                    var options = sp.GetRequiredService<IOptions<ResendOptions>>().Value;

                    client.BaseAddress = new Uri("https://api.resend.com/");
                    client.Timeout = TimeSpan.FromSeconds(Math.Max(1, options.TimeoutSeconds));
                    client.DefaultRequestHeaders.Authorization =
                        new AuthenticationHeaderValue("Bearer", options.ApiKey);
                });

                return;
            }

            services.AddSingleton<INotificationSender>(sp =>
            {
                sp.GetRequiredService<ILogger<UnconfiguredSender>>().LogWarning(
                    "Email:Provider vaut « resend » mais Resend:ApiKey ou Resend:From manque. "
                    + "Les recus de course seront consignes « ignore » et ne partiront pas. "
                    + "RAPPEL : le domaine de l'expediteur doit etre verifie chez Resend (DKIM, SPF) "
                    + "avant qu'un seul message ne parte.");

                return new UnconfiguredSender(NotificationChannel.Email);
            });

            return;
        }

        if (string.Equals(fournisseur, "smtp", StringComparison.OrdinalIgnoreCase))
        {
            var hote = configuration[$"{SmtpOptions.SectionName}:Host"];
            var expediteur = configuration[$"{SmtpOptions.SectionName}:From"];

            if (!string.IsNullOrWhiteSpace(hote) && !string.IsNullOrWhiteSpace(expediteur))
            {
                services.AddSingleton<INotificationSender, SmtpEmailSender>();
                return;
            }

            services.AddSingleton<INotificationSender>(sp =>
            {
                sp.GetRequiredService<ILogger<UnconfiguredSender>>().LogWarning(
                    "Email:Provider vaut « smtp » mais Smtp:Host ou Smtp:From manque : "
                    + "aucun relais de courriel. Les recus seront consignes « ignore ».");

                return new UnconfiguredSender(NotificationChannel.Email);
            });

            return;
        }

        // IL FAUT DIRE QUELLE VALEUR A ETE LUE, PAS SEULEMENT QU'IL N'Y EN A
        // PAS. Meme lecon que sur le SMS : une faute de frappe dans
        // EMAIL_PROVIDER et un conteneur cree avant que le .env soit rempli
        // donnent tous deux un service qui demarre sans broncher.
        services.AddSingleton<INotificationSender>(sp =>
        {
            sp.GetRequiredService<ILogger<UnconfiguredSender>>().LogWarning(
                "Email:Provider vaut « {Fournisseur} » : aucun expediteur de courriel n'est "
                + "enregistre. Attendu « resend » ou « smtp ». Les recus de course seront "
                + "consignes « ignore ». Si le .env est correct, le conteneur a ete cree avant : "
                + "docker compose up -d --force-recreate.",
                fournisseur);

            return new UnconfiguredSender(NotificationChannel.Email);
        });
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
