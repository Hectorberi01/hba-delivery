using Hba.BuildingBlocks.Application.Abstractions;
using Hba.BuildingBlocks.Messaging.Inbox;
using Hba.BuildingBlocks.Messaging.Outbox;
using Hba.BuildingBlocks.Persistence;
using Hba.BuildingBlocks.Persistence.Configurations;
using Hba.Pricing.Domain.Quotes;
using Hba.Pricing.Domain.Tariffs;
using Hba.Pricing.Domain.Zones;
using Microsoft.EntityFrameworkCore;

namespace Hba.Pricing.Infrastructure.Persistence;

/// <summary>
/// Base du service Pricing. Une base par service : aucun autre service n'y
/// accede, meme en lecture.
/// </summary>
public sealed class PricingDbContext(DbContextOptions<PricingDbContext> options)
    : DbContext(options), IUnitOfWork
{
    public const string Schema = "pricing";

    public static readonly EfMessagingOptions MessagingTables = new() { Schema = Schema };

    public DbSet<Tariff> Tariffs => Set<Tariff>();

    public DbSet<Zone> Zones => Set<Zone>();

    public DbSet<Quote> Quotes => Set<Quote>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();

    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(PricingDbContext).Assembly);
        modelBuilder.ApplyHbaMessagingModel(MessagingTables);
    }
}
