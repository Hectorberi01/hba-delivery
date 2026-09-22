using Hba.BuildingBlocks.Application.Abstractions;
using Hba.BuildingBlocks.Messaging.Extensions;
using Hba.BuildingBlocks.Messaging.Inbox;
using Hba.BuildingBlocks.Messaging.Outbox;
using Hba.BuildingBlocks.Persistence;
using Hba.Identity.Application.Ports;
using Hba.Identity.Infrastructure.Messaging;
using Hba.Identity.Infrastructure.Otp;
using Hba.Identity.Infrastructure.Persistence;
using Hba.Identity.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace Hba.Identity.Infrastructure.Extensions;

public static class IdentityInfrastructureExtensions
{
    public static IServiceCollection AddIdentityInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddDbContext<IdentityDbContext>(options =>
            options.UseNpgsql(
                configuration.GetConnectionString("IdentityDb"),
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

        AddRedis(services, configuration);
        AddSecurity(services, configuration);

        return services;
    }

    private static void AddRedis(IServiceCollection services, IConfiguration configuration)
    {
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
    }
}
