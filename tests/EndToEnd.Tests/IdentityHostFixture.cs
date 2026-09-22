using Grpc.Net.Client;
using Hba.Identity.Api;
using Hba.Identity.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;
using Xunit;

namespace Hba.EndToEnd.Tests;

/// <summary>
/// Un hôte Identity réel, avec un vrai PostgreSQL et un vrai Redis. Rien n'est
/// simulé du côté qui compte : le hachage des mots de passe, la signature des
/// jetons, la limitation de débit et la persistance sont ceux de production.
///
/// Kafka, en revanche, n'est pas démarré : le service de publication de
/// l'Outbox est retiré. Ce que le test vérifie, c'est que le message d'envoi
/// est bien DÉPOSÉ, pas que Kafka fonctionne.
/// </summary>
public sealed class IdentityHostFixture : WebApplicationFactory<IdentityApi>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("hba_identity")
        .WithUsername("hba")
        .WithPassword("hba")
        .Build();

    private readonly RedisContainer _redis = new RedisBuilder()
        .WithImage("redis:7-alpine")
        .Build();

    /// <summary>Code fixe : le test ne peut pas lire un SMS.</summary>
    public const string FixedOtpCode = "424242";

    public const string Issuer = "https://identity.tests.hba";

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_postgres.StartAsync(), _redis.StartAsync());

        using var scope = Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        await context.Database.EnsureCreatedAsync();
    }

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        await Task.WhenAll(_postgres.DisposeAsync().AsTask(), _redis.DisposeAsync().AsTask());
    }

    /// <summary>
    /// Ouvre une portée, prête le contexte, la referme. Rendre le contexte
    /// lui-même laisserait la portée ouverte et ferait fuir des connexions au
    /// fil des tests.
    /// </summary>
    public async Task WithDbAsync(Func<IdentityDbContext, Task> work)
    {
        ArgumentNullException.ThrowIfNull(work);

        using var scope = Services.CreateScope();
        await work(scope.ServiceProvider.GetRequiredService<IdentityDbContext>());
    }

    /// <summary>Canal gRPC branché sur le serveur de test, en HTTP/2.</summary>
    public GrpcChannel CreateGrpcChannel()
        => GrpcChannel.ForAddress(
            Server.BaseAddress,
            new GrpcChannelOptions { HttpHandler = Server.CreateHandler() });

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.UseEnvironment(Environments.Development);

        builder.UseSetting("ConnectionStrings:IdentityDb", _postgres.GetConnectionString());
        builder.UseSetting("ConnectionStrings:Redis", _redis.GetConnectionString());

        builder.UseSetting("Database:AutoMigrate", "false");

        // Aucune migration n'est versionnée pour l'instant : le schéma est créé
        // par EnsureCreated. À remplacer par AutoMigrate dès que la première
        // migration existe.
        builder.UseSetting("Tokens:Issuer", Issuer);
        builder.UseSetting("Tokens:Audience", "hba-delivery");
        builder.UseSetting("Jwt:Issuer", Issuer);
        builder.UseSetting("Jwt:RequireHttpsMetadata", "false");

        builder.UseSetting("JwtSigning:AllowEphemeralKey", "true");
        builder.UseSetting("DataProtection:AllowDevelopmentKey", "true");

        builder.UseSetting("Otp:AllowDevelopmentPepper", "true");
        builder.UseSetting("Otp:FixedCodeForDevelopment", FixedOtpCode);
        builder.UseSetting("Otp:ResendCooldownSeconds", "60");
        builder.UseSetting("Otp:MaxPerHour", "5");

        // Pas d'amorçage d'administrateur : chaque test crée ce dont il a besoin.
        builder.UseSetting("Bootstrap:AdminEmail", string.Empty);
        builder.UseSetting("Bootstrap:AdminPassword", string.Empty);

        builder.ConfigureServices(services =>
        {
            // Sans courtier Kafka, le dispatcher d'Outbox tournerait en boucle
            // sur des échecs de connexion et polluerait la sortie du test.
            services.RemoveAll<IHostedService>();
        });
    }
}

[CollectionDefinition(Name)]
public sealed class IdentityCollection : ICollectionFixture<IdentityHostFixture>
{
    public const string Name = "identity-host";
}
