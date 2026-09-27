using Hba.Payment.Domain.Payments;
using Hba.Payment.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hba.Payment.Infrastructure.Persistence.Configurations;

internal sealed class PaymentIntentConfiguration : IEntityTypeConfiguration<PaymentIntent>
{
    public void Configure(EntityTypeBuilder<PaymentIntent> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("payment_intents");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).HasColumnName("id");
        builder.Property(p => p.DeliveryId).HasColumnName("delivery_id");
        builder.Property(p => p.PayerId).HasColumnName("payer_id").HasMaxLength(64).IsRequired();
        builder.Property(p => p.PayerPhone).HasColumnName("payer_phone").HasMaxLength(32);

        // Entier de francs CFA : le XOF n'a pas de subdivision (ADR 0006), et
        // FedaPay attend le meme entier.
        builder.Property(p => p.Amount)
            .HasColumnName("amount")
            .HasConversion(money => money.Amount, amount => MoneyXof.From(amount))
            .HasColumnType("bigint")
            .IsRequired();

        builder.Property(p => p.Status).HasColumnName("status").IsRequired();
        builder.Property(p => p.Method).HasColumnName("method").IsRequired();
        builder.Property(p => p.ProviderReference).HasColumnName("provider_reference").HasMaxLength(128);
        builder.Property(p => p.RedirectUrl).HasColumnName("redirect_url").HasMaxLength(512);
        builder.Property(p => p.FailureReason).HasColumnName("failure_reason").HasMaxLength(512);
        builder.Property(p => p.CreatedAt).HasColumnName("created_at");
        builder.Property(p => p.SucceededAt).HasColumnName("succeeded_at");
        builder.Property(p => p.ExpiresAt).HasColumnName("expires_at");

        // LE JETON DE CONCURRENCE N'EST PAS DECORATIF ICI. Le webhook du
        // fournisseur peut arriver deux fois en parallele : sans xmin, deux
        // transactions concurrentes pourraient toutes deux passer l'intention
        // a « payee » et publier deux fois l'evenement.
        builder.Property(p => p.Version).IsRowVersion().HasColumnName("xmin").HasColumnType("xid");

        // LA RECHERCHE DU CHEMIN WEBHOOK. Unique : deux intentions ne peuvent
        // pas pointer la meme transaction chez le fournisseur.
        builder.HasIndex(p => p.ProviderReference).IsUnique();

        builder.HasIndex(p => p.DeliveryId);

        // DEUX INDEX, PARCE QU'IL Y A DEUX LECTURES (ADR 0019). La cohorte se
        // lit par date d'ouverture, la recette par date d'encaissement : un
        // seul des deux laisserait l'autre balayer la table.
        builder.HasIndex(p => p.CreatedAt);
        builder.HasIndex(p => p.SucceededAt);

        builder.Ignore(p => p.IsSettled);
    }
}
