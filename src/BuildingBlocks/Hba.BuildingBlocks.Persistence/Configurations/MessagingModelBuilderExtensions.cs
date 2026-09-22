using Hba.BuildingBlocks.Messaging.Inbox;
using Hba.BuildingBlocks.Messaging.Outbox;
using Microsoft.EntityFrameworkCore;

namespace Hba.BuildingBlocks.Persistence.Configurations;

public static class MessagingModelBuilderExtensions
{
    /// <summary>
    /// Cartographie des trois tables techniques communes à tous les services :
    /// Outbox, Inbox, clés d'idempotence. Un seul endroit, sept bases.
    /// </summary>
    public static ModelBuilder ApplyHbaMessagingModel(this ModelBuilder modelBuilder, EfMessagingOptions options)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        ArgumentNullException.ThrowIfNull(options);

        modelBuilder.Entity<OutboxMessage>(builder =>
        {
            builder.ToTable(options.OutboxTable, options.Schema);
            builder.HasKey(m => m.Id);

            builder.Property(m => m.Topic).HasMaxLength(128).IsRequired();
            builder.Property(m => m.PartitionKey).HasMaxLength(128).IsRequired();
            builder.Property(m => m.EventType).HasMaxLength(200).IsRequired();
            builder.Property(m => m.Payload).IsRequired();
            builder.Property(m => m.LastError).HasMaxLength(1000);
            builder.Property(m => m.TraceId).HasMaxLength(64);
            builder.Property(m => m.CorrelationId).HasMaxLength(64);

            // Index partiel : seule la file non publiée est parcourue, et elle
            // reste petite même quand la table grossit.
            builder.HasIndex(m => new { m.PublishedAt, m.NextAttemptAt })
                .HasFilter("\"PublishedAt\" IS NULL");
        });

        modelBuilder.Entity<InboxMessage>(builder =>
        {
            builder.ToTable(options.InboxTable, options.Schema);
            builder.HasKey(m => m.EventId);

            builder.Property(m => m.EventType).HasMaxLength(200).IsRequired();
            builder.Property(m => m.Topic).HasMaxLength(128).IsRequired();
            builder.Property(m => m.LastError).HasMaxLength(1000);
        });

        modelBuilder.Entity<IdempotencyRecord>(builder =>
        {
            builder.ToTable(options.IdempotencyTable, options.Schema);
            builder.HasKey(r => new { r.Scope, r.Key });

            builder.Property(r => r.Scope).HasMaxLength(64);
            builder.Property(r => r.Key).HasMaxLength(200);
            builder.Property(r => r.ResourceId).HasMaxLength(64).IsRequired();
        });

        return modelBuilder;
    }
}
