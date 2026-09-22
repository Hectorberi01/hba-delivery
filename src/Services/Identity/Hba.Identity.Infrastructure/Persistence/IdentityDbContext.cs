using Hba.BuildingBlocks.Application.Abstractions;
using Hba.BuildingBlocks.Domain;
using Hba.BuildingBlocks.Messaging.Inbox;
using Hba.BuildingBlocks.Messaging.Outbox;
using Hba.BuildingBlocks.Persistence;
using Hba.BuildingBlocks.Persistence.Configurations;
using Hba.Identity.Application.IntegrationEvents;
using Hba.Identity.Domain.Accounts;
using Hba.Identity.Domain.Partners;
using Hba.Identity.Domain.Sessions;
using Microsoft.EntityFrameworkCore;

namespace Hba.Identity.Infrastructure.Persistence;

/// <summary>
/// Base du service Identity. Elle contient des secrets : empreintes de mots de
/// passe, empreintes de jetons, secrets partenaires chiffrés. Aucun autre
/// service n'y accède, et rien n'en sort en clair par une requête.
/// </summary>
public sealed class IdentityDbContext(
    DbContextOptions<IdentityDbContext> options,
    IIdentityIntegrationEventPublisher integrationEvents) : DbContext(options), IUnitOfWork
{
    public const string Schema = "identity";

    public static readonly EfMessagingOptions MessagingTables = new() { Schema = Schema };

    public DbSet<Account> Accounts => Set<Account>();

    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    public DbSet<PartnerClient> PartnerClients => Set<PartnerClient>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();

    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(IdentityDbContext).Assembly);
        modelBuilder.ApplyHbaMessagingModel(MessagingTables);
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        // L'Outbox est construite sur CE contexte : l'injecter dans le
        // publieur formerait un cycle, puisque EfOutbox écrit dans le
        // DbContext qui a besoin du publieur.
        var outbox = new EfOutbox(this);

        Drain<Account>((aggregate, domainEvent) => integrationEvents.Publish(outbox, aggregate, domainEvent));
        Drain<PartnerClient>((aggregate, domainEvent) => integrationEvents.Publish(outbox, aggregate, domainEvent));

        return await base.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private void Drain<TAggregate>(Action<TAggregate, IDomainEvent> publish)
        where TAggregate : AggregateRoot
    {
        var aggregates = ChangeTracker
            .Entries<TAggregate>()
            .Select(entry => entry.Entity)
            .Where(aggregate => aggregate.DomainEvents.Count > 0)
            .ToList();

        foreach (var aggregate in aggregates)
        {
            IDomainEvent[] events = [.. aggregate.DomainEvents];
            aggregate.ClearDomainEvents();

            foreach (var domainEvent in events)
            {
                publish(aggregate, domainEvent);
            }
        }
    }
}
