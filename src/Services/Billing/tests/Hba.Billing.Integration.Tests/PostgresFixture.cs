using Hba.Billing.Application.IntegrationEvents;
using Hba.Billing.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Testcontainers.PostgreSql;
using Xunit;

namespace Hba.Billing.Integration.Tests;

/// <summary>
/// Un PostgreSQL réel et jetable.
/// </summary>
///
/// <remarks>
/// UNE VRAIE BASE, ET IL N'Y A PAS D'ALTERNATIVE ICI. Ce qui est testé dans ce
/// projet — un verrou de ligne « FOR UPDATE » et deux contraintes d'unicité —
/// n'existe QUE dans PostgreSQL. Le fournisseur en mémoire d'EF Core ignore les
/// index uniques et n'a aucune notion de verrou : les mêmes tests y passeraient
/// tous, y compris après suppression de ce qu'ils vérifient.
///
/// MIGRATE ET NON EnsureCreated. La migration existe ; la créer par le modèle
/// laisserait passer une migration fausse — c'est-à-dire exactement ce qui
/// casse en production et nulle part ailleurs.
///
/// PAS D'IMAGE POSTGIS, contrairement à Delivery : Billing ne stocke aucune
/// géométrie. L'image simple démarre plus vite.
///
/// UNE SEULE BASE POUR TOUTES LES CLASSES DE TEST, ET DEUX RÈGLES QUI VONT AVEC.
/// En démarrer un conteneur par classe coûterait quinze secondes à chaque fois ;
/// le prix de ce choix est que les tests se voient les uns les autres. Le premier
/// passage réel l'a rappelé de deux façons : une requête « tous les messages du
/// topic billing » en trouvait deux au lieu d'un, et deux tests partageaient une
/// clé d'idempotence, dont l'index est unique dans TOUTE la table.
///
/// Donc, dans ce projet :
///
/// 1. **Chaque test a ses propres identifiants** — son commerçant, ses clés
///    d'idempotence. Elles portent un préfixe qui dit à quel test elles
///    appartiennent, pour que la prochaine collision se voie à la lecture.
/// 2. **Toute requête d'inventaire est filtrée** sur l'objet du test : la clé de
///    partition de l'Outbox, l'identifiant du compte. Jamais « tout ce qu'il y a
///    dans la table ».
/// </remarks>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("hba_billing")
        .WithUsername("hba")
        .WithPassword("hba")
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        await using var context = CreateContext();
        await context.Database.MigrateAsync();
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    /// <summary>
    /// Le publieur d'evenements d'integration est simule : ces tests portent sur
    /// la persistance et le verrou, pas sur Kafka.
    /// </summary>
    public BillingDbContext CreateContext(IBillingIntegrationEventPublisher? publisher = null)
    {
        var options = new DbContextOptionsBuilder<BillingDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;

        return new BillingDbContext(options, publisher ?? Substitute.For<IBillingIntegrationEventPublisher>());
    }
}

[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "postgres";
}
