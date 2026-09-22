using Hba.BuildingBlocks.Application.Abstractions;
using Hba.BuildingBlocks.Domain;
using Hba.BuildingBlocks.Messaging.Inbox;
using Hba.BuildingBlocks.Messaging.Outbox;
using Hba.BuildingBlocks.Persistence;
using Hba.BuildingBlocks.Persistence.Configurations;
using Hba.Delivery.Application.IntegrationEvents;
using Microsoft.EntityFrameworkCore;

namespace Hba.Delivery.Infrastructure.Persistence;

/// <summary>
/// Base du service Delivery. Une base par service : aucun autre service n'y
/// accède, même en lecture.
/// </summary>
public sealed class DeliveryDbContext(
    DbContextOptions<DeliveryDbContext> options,
    IDeliveryIntegrationEventPublisher integrationEvents) : DbContext(options), IUnitOfWork
{
    public const string Schema = "delivery";

    public static readonly EfMessagingOptions MessagingTables = new() { Schema = Schema };

    public DbSet<DeliveryAggregate> Deliveries => Set<DeliveryAggregate>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();

    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(DeliveryDbContext).Assembly);
        modelBuilder.ApplyHbaMessagingModel(MessagingTables);
    }

    /// <summary>
    /// Les faits de domaine sont traduits en messages d'Outbox JUSTE AVANT le
    /// commit : ils partagent donc la transaction du changement métier.
    /// </summary>
    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        DrainDomainEvents();
        return await base.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private void DrainDomainEvents()
    {
        // L'Outbox est construite ici, sur CE contexte, plutôt qu'injectée dans
        // le publieur : EfOutbox écrit dans le DbContext, et le DbContext a
        // besoin du publieur. Les deux en injection formeraient un cycle.
        var outbox = new EfOutbox(this);

        var aggregates = ChangeTracker
            .Entries<DeliveryAggregate>()
            .Select(entry => entry.Entity)
            .Where(aggregate => aggregate.DomainEvents.Count > 0)
            .ToList();

        foreach (var aggregate in aggregates)
        {
            IDomainEvent[] events = [.. aggregate.DomainEvents];
            aggregate.ClearDomainEvents();

            foreach (var domainEvent in events)
            {
                integrationEvents.Publish(outbox, aggregate, domainEvent);
            }
        }
    }
}
