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
                configuration.GetConnectionString("NotificationDb"),
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

        if (string.Equals(provider, "log", StringComparison.OrdinalIgnoreCase) && environment.IsDevelopment())
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
                services.AddSingleton<INotificationSender>(_ => new UnconfiguredSender(NotificationChannel.Sms));
            }
        }

        // WhatsApp et le push n'ont pas de fournisseur : ils sont déclarés pour
        // que le journal distingue « pas de fournisseur » d'« erreur d'envoi ».
        services.AddSingleton<INotificationSender>(_ => new UnconfiguredSender(NotificationChannel.WhatsApp));
        services.AddSingleton<INotificationSender>(_ => new UnconfiguredSender(NotificationChannel.Push));
    }
}
