using Hba.BuildingBlocks.Application.Abstractions;
using Hba.BuildingBlocks.Messaging.Inbox;
using Hba.BuildingBlocks.Messaging.Outbox;
using Hba.BuildingBlocks.Persistence;
using Hba.BuildingBlocks.Persistence.Configurations;
using Hba.Rating.Domain.Reviews;
using Microsoft.EntityFrameworkCore;

namespace Hba.Rating.Infrastructure.Persistence;

/// <summary>
/// Rating consomme les fins de mission et ne publie rien — pour l'instant.
/// </summary>
///
/// <remarks>
/// LA TABLE D'INBOX EST CELLE QUI COMPTE ICI : Kafka rejoue, et une notation
/// ouverte deux fois se verrait tout de suite a l'ecran du client. L'Outbox
/// existe quand meme, comme chez Notification, parce que le jour ou une note
/// soumise devra etre publiee — a Driver, a Dispatch — il ne faudra pas remonter
/// toute la plomberie.
/// </remarks>
public sealed class RatingDbContext(DbContextOptions<RatingDbContext> options)
    : DbContext(options), IUnitOfWork
{
    public const string Schema = "rating";

    public static readonly EfMessagingOptions MessagingTables = new() { Schema = Schema };

    public DbSet<Review> Reviews => Set<Review>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();

    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(RatingDbContext).Assembly);
        modelBuilder.ApplyHbaMessagingModel(MessagingTables);
    }
}
