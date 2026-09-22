using Hba.BuildingBlocks.Application.Abstractions;
using Hba.BuildingBlocks.Messaging.Inbox;
using Hba.BuildingBlocks.Messaging.Outbox;
using Hba.BuildingBlocks.Persistence;
using Hba.BuildingBlocks.Persistence.Configurations;
using Hba.Notification.Domain.Messages;
using Microsoft.EntityFrameworkCore;

namespace Hba.Notification.Infrastructure.Persistence;

/// <summary>
/// Notification ne publie rien : elle consomme. La table d'Outbox existe quand
/// même, parce que le jour où un accusé de réception d'opérateur devra être
/// republié, il ne faudra pas remonter toute la plomberie.
/// </summary>
public sealed class NotificationDbContext(DbContextOptions<NotificationDbContext> options)
    : DbContext(options), IUnitOfWork
{
    public const string Schema = "notification";

    public static readonly EfMessagingOptions MessagingTables = new() { Schema = Schema };

    public DbSet<SentNotification> SentNotifications => Set<SentNotification>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();

    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(NotificationDbContext).Assembly);
        modelBuilder.ApplyHbaMessagingModel(MessagingTables);
    }
}
