using Hba.BuildingBlocks.Application.Abstractions;
using Hba.BuildingBlocks.Messaging.Extensions;
using Hba.BuildingBlocks.Messaging.Inbox;
using Hba.BuildingBlocks.Messaging.Outbox;
using Hba.BuildingBlocks.Persistence;
using Hba.Identity.Application.Common.Interfaces;
using Hba.Identity.Domain.Interfaces;
using Hba.Identity.Infrastructure.Persistence;
using Hba.Identity.Infrastructure.Persistence.Repositories;
using Hba.Identity.Infrastructure.Services.Messaging;
using Hba.Identity.Infrastructure.Services.Otp;
using Hba.Identity.Infrastructure.Services.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using StackExchange.Redis;

namespace Hba.Identity.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddIdentityInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        services.AddDbContext<IdentityDbContext>(options =>
            options.UseNpgsql(
                configuration.Obligatoire("IdentityDb"),
                npgsql => npgsql.MigrationsHistoryTable("__ef_migrations", IdentityDbContext.Schema)));

        services.AddHbaAutoMigration<IdentityDbContext>();
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<IdentityDbContext>());
        services.AddScoped<IAccountRepository, AccountRepository>();
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
        services.AddScoped<IPartnerClientRepository, PartnerClientRepository>();

        services.AddSingleton(IdentityDbContext.MessagingTables);
        services.AddScoped<IOutbox>(sp => new EfOutbox(sp.GetRequiredService<IdentityDbContext>()));
        services.AddScoped<IOutboxStore>(sp => new EfOutboxStore(
            sp.GetRequiredService<IdentityDbContext>(),
            IdentityDbContext.MessagingTables));
        services.AddScoped<IInboxStore>(sp => new EfInboxStore(sp.GetRequiredService<IdentityDbContext>()));
        services.AddScoped<IIdempotencyStore>(sp => new EfIdempotencyStore(sp.GetRequiredService<IdentityDbContext>()));

        services.AddHbaMessaging(configuration, producerName: "identity", consumerGroupId: "identity");
        services.AddScoped<IOtpNotifier, OutboxOtpNotifier>();

        AddRedis(services, configuration, environment);
        AddSecurity(services, configuration);

        return services;
    }

    private static void AddRedis(
        IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        // REFUS EXPLICITE PLUTOT QUE DEGRADATION SILENCIEUSE. Otp:FixedCodeForDevelopment
        // fait renvoyer la MEME valeur a chaque demande. En developpement c'est
        // une commodite ; ailleurs, c'est une authentification qui n'en est plus
        // une : quiconque connait la constante entre dans n'importe quel compte,
        // et rien dans les journaux ne distingue ce cas d'un fonctionnement
        // normal. Le service refuse donc de demarrer plutot que de demarrer faux.
        //
        // Le meme choix est deja fait dans Notification pour l'operateur SMS
        // « log », qui ecrirait les codes en clair dans les journaux.
        var fixedCode = configuration[$"{OtpOptions.SectionName}:FixedCodeForDevelopment"];

        if (!string.IsNullOrWhiteSpace(fixedCode) && !environment.IsDevelopment())
        {
            throw new InvalidOperationException(
                $"Otp:FixedCodeForDevelopment est renseigné en environnement {environment.EnvironmentName} : "
                + "tous les codes de connexion vaudraient la même valeur. Retirez ce réglage.");
        }

        var connectionString = configuration.GetConnectionString("Redis") ?? "localhost:6379";

        services.AddSingleton<IConnectionMultiplexer>(
            _ => ConnectionMultiplexer.Connect(connectionString));

        services.AddOptions<OtpOptions>()
            .Bind(configuration.GetSection(OtpOptions.SectionName))
            .ValidateOnStart();

        services.AddSingleton<IOtpCodeService, OtpCodeService>();
        services.AddScoped<IOtpStore, RedisOtpStore>();
        services.AddScoped<IOtpRateLimiter, RedisOtpRateLimiter>();
    }

    private static void AddSecurity(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<JwtSigningOptions>()
            .Bind(configuration.GetSection(JwtSigningOptions.SectionName))
            .ValidateOnStart();

        services.AddOptions<TokenOptions>()
            .Bind(configuration.GetSection(TokenOptions.SectionName))
            .ValidateOnStart();

        services.AddOptions<DataProtectionOptions>()
            .Bind(configuration.GetSection(DataProtectionOptions.SectionName))
            .ValidateOnStart();

        // La clé de signature est chargée une fois : c'est elle qui donne le kid
        // annoncé dans les JWKS.
        services.AddSingleton<ISigningKeyProvider, RsaSigningKeyProvider>();
        services.AddSingleton<ITokenIssuer, JwtTokenIssuer>();
        services.AddSingleton<ISecretProtector, AesGcmSecretProtector>();
        services.AddSingleton<IClientCredentialsFactory, ClientCredentialsFactory>();

        // LES SERVICES INTERNES VIENNENT DE LA CONFIGURATION, PAS DE LA BASE.
        // Voir ADR 0018. La liste peut etre vide : un deploiement ou aucun
        // service n'appelle depuis un consommateur n'a rien a declarer, et
        // IssueServiceToken refusera alors tout le monde.
        services.AddOptions<ServiceClientOptions>()
            .Bind(configuration.GetSection(ServiceClientOptions.SectionName))
            .ValidateOnStart();

        services.AddSingleton<IServiceClientRegistry, ConfiguredServiceClientRegistry>();
    }
}
