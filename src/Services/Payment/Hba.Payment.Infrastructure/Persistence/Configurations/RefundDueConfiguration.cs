using Hba.Payment.Domain.Refunds;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hba.Payment.Infrastructure.Persistence.Configurations;

internal sealed class RefundDueConfiguration : IEntityTypeConfiguration<RefundDue>
{
    public void Configure(EntityTypeBuilder<RefundDue> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("refunds_due");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).HasColumnName("id");

        builder.Property(r => r.ServiceType).HasColumnName("service_type").HasConversion<int>();
        builder.Property(r => r.ReferenceId).HasColumnName("reference_id");
        builder.Property(r => r.PaymentIntentId).HasColumnName("payment_intent_id");
        builder.Property(r => r.PayerId).HasColumnName("payer_id").HasMaxLength(64).IsRequired();

        builder.Property(r => r.AmountCollected).HasColumnName("amount_collected").HasColumnType("bigint");

        // NULLABLE, ET C'EST LE POINT DE CE SCHEMA. Nul veut dire « a determiner » —
        // la politique d'annulation n'est pas tranchee (point 3). Un zero se lirait
        // « rien n'est du », ce qui est une decision que personne n'a prise.
        builder.Property(r => r.RecommendedRefund).HasColumnName("recommended_refund").HasColumnType("bigint");

        builder.Property(r => r.Reason).HasColumnName("reason").HasMaxLength(64).IsRequired();
        builder.Property(r => r.Status).HasColumnName("status").HasConversion<int>();
        builder.Property(r => r.SettledAmount).HasColumnName("settled_amount").HasColumnType("bigint");
        builder.Property(r => r.CreatedAt).HasColumnName("created_at");
        builder.Property(r => r.ResolvedAt).HasColumnName("resolved_at");
        builder.Property(r => r.ResolvedBy).HasColumnName("resolved_by").HasMaxLength(64);
        builder.Property(r => r.Note).HasColumnName("note").HasMaxLength(500);

        // LE JETON DE CONCURRENCE COMPTE ICI, parce que deux personnes de la finance
        // peuvent ouvrir la meme ligne en meme temps. Sans lui, la seconde ecraserait
        // la decision de la premiere, et le journal porterait deux montants rendus
        // pour un seul virement.
        builder.Property(r => r.Version).IsRowVersion().HasColumnName("xmin").HasColumnType("xid");

        // UN ENCAISSEMENT NE PRODUIT QU'UNE LIGNE DE FILE, ET LA BASE LE GARANTIT.
        //
        // Le controle applicatif lit avant d'ecrire ; une course annulee PUIS
        // declaree sans chauffeur produit deux evenements, et deux consommateurs
        // concurrents peuvent conclure tous deux que la ligne manque. Cet index les
        // departage — sinon la finance rembourserait deux fois, et c'est de l'argent
        // qui part.
        builder.HasIndex(r => r.PaymentIntentId)
            .IsUnique()
            .HasDatabaseName("ux_refunds_due_intention");

        // LA LECTURE DE L'ECRAN : ce qui reste a faire, du plus ancien au plus
        // recent. L'index porte le statut, qui est le filtre, puis la date, qui est
        // le tri.
        builder.HasIndex(r => new { r.Status, r.CreatedAt });

        builder.Ignore(r => r.EstOuvert);
    }
}
