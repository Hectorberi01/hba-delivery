using Hba.BuildingBlocks.Application.Abstractions;
using Hba.BuildingBlocks.Domain;
using Hba.BuildingBlocks.Messaging.Inbox;
using Hba.BuildingBlocks.Messaging.Outbox;
using Hba.BuildingBlocks.Persistence;
using Hba.BuildingBlocks.Persistence.Configurations;
using Hba.Dispatch.Application.Common.IntegrationEvents;
using Hba.Dispatch.Domain.Dispatching;
using Microsoft.EntityFrameworkCore;

namespace Hba.Dispatch.Infrastructure.Persistence;

public sealed class DispatchDbContext(
    DbContextOptions<DispatchDbContext> options,
    IDispatchIntegrationEventPublisher integrationEvents) : DbContext(options), IUnitOfWork
{
    public const string Schema = "dispatch";

    public static readonly EfMessagingOptions MessagingTables = new() { Schema = Schema };

    public DbSet<DispatchAggregate> Dispatches => Set<DispatchAggregate>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();

    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(DispatchDbContext).Assembly);
        modelBuilder.ApplyHbaMessagingModel(MessagingTables);
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        DrainDomainEvents();
        return await base.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private void DrainDomainEvents()
    {
        var outbox = new EfOutbox(this);

        var aggregates = ChangeTracker
            .Entries<DispatchAggregate>()
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
