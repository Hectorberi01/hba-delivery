using Hba.Notification.Domain.Messages;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hba.Notification.Infrastructure.Persistence.Configurations;

internal sealed class SentNotificationConfiguration : IEntityTypeConfiguration<SentNotification>
{
    public void Configure(EntityTypeBuilder<SentNotification> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("sent_notifications");
        builder.HasKey(n => n.Id);

        builder.Ignore(n => n.DomainEvents);

        builder.Property(n => n.Version).IsRowVersion().HasColumnName("xmin").HasColumnType("xid");

        builder.Property(n => n.Channel).HasConversion<int>().IsRequired();
        builder.Property(n => n.Status).HasConversion<int>().IsRequired();

        builder.Property(n => n.Recipient).HasColumnName("recipient").HasMaxLength(64).IsRequired();
        builder.Property(n => n.TemplateId).HasColumnName("template_id").HasMaxLength(64).IsRequired();
        builder.Property(n => n.Provider).HasColumnName("provider").HasMaxLength(64);
        builder.Property(n => n.ProviderMessageId).HasColumnName("provider_message_id").HasMaxLength(128);
        builder.Property(n => n.Error).HasColumnName("error").HasMaxLength(500);
        builder.Property(n => n.CorrelationId).HasColumnName("correlation_id").HasMaxLength(64);

        // Les deux questions qu'on pose à ce journal : « qu'a reçu ce numéro ? »
        // et « qu'est-ce qui a échoué aujourd'hui ? ».
        builder.HasIndex(n => new { n.Recipient, n.CreatedAt });
        builder.HasIndex(n => new { n.Status, n.CreatedAt });
    }
}
