using Hba.Delivery.Application.IntegrationEvents;
using Hba.Delivery.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Testcontainers.PostgreSql;
using Xunit;

namespace Hba.Delivery.Integration.Tests;

/// <summary>
/// Un PostgreSQL réel, jetable. Le schéma est créé par EnsureCreated tant que la
/// première migration n'est pas générée ; il faudra basculer sur Migrate().
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgis/postgis:16-3.4-alpine")
        .WithDatabase("hba_delivery")
        .WithUsername("hba")
        .WithPassword("hba")
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        await using var context = CreateContext();
        await context.Database.EnsureCreatedAsync();
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    /// <summary>
    /// Le publisher d'événements d'intégration est simulé : ces tests portent sur
    /// la persistance, pas sur Kafka.
    /// </summary>
    public DeliveryDbContext CreateContext(IDeliveryIntegrationEventPublisher? publisher = null)
    {
        var options = new DbContextOptionsBuilder<DeliveryDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;

        return new DeliveryDbContext(options, publisher ?? Substitute.For<IDeliveryIntegrationEventPublisher>());
    }
}

[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "postgres";
}
