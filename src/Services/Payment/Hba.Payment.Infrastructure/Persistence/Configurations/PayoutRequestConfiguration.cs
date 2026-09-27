using Hba.Payment.Domain.Payouts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hba.Payment.Infrastructure.Persistence.Configurations;

internal sealed class PayoutRequestConfiguration : IEntityTypeConfiguration<PayoutRequest>
{
    public void Configure(EntityTypeBuilder<PayoutRequest> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("payout_requests");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).HasColumnName("id");
        builder.Property(p => p.DriverId).HasColumnName("driver_id").HasMaxLength(64).IsRequired();
        builder.Property(p => p.AmountXof).HasColumnName("amount_xof").HasColumnType("bigint").IsRequired();
        builder.Property(p => p.Status).HasColumnName("status").HasConversion<int>().IsRequired();
        builder.Property(p => p.RequestedAt).HasColumnName("requested_at").IsRequired();
        builder.Property(p => p.DecidedAt).HasColumnName("decided_at");
        builder.Property(p => p.DecidedBy).HasColumnName("decided_by").HasMaxLength(64);
        builder.Property(p => p.RejectionReason).HasColumnName("rejection_reason").HasMaxLength(500);
        builder.Property(p => p.PaidAt).HasColumnName("paid_at");
        builder.Property(p => p.PaymentReference).HasColumnName("payment_reference").HasMaxLength(128);

        builder.Ignore(p => p.IsPending);

        // LE JETON DE CONCURRENCE EST INDISPENSABLE ICI. Deux operateurs de la
        // finance qui ouvrent la meme demande et cliquent en meme temps
        // produiraient deux approbations, donc potentiellement deux virements
        // pour une seule demande. Le xmin en arrete un.
        builder.Property(p => p.Version).IsRowVersion().HasColumnName("xmin").HasColumnType("xid");

        // UNE SEULE DEMANDE EN COURS PAR LIVREUR, ET LA BASE LE GARANTIT.
        //
        // Le controle applicatif lit avant d'ecrire ; deux requetes simultanees
        // peuvent lire ensemble et conclure toutes deux qu'il n'y a rien. Cet
        // index les departage. Le filtre porte sur les deux etats vivants :
        // une fois versee ou refusee, une demande ne gene plus les suivantes.
        builder.HasIndex(p => p.DriverId)
            .IsUnique()
            .HasFilter("status IN (1, 2)")
            .HasDatabaseName("ix_payout_requests_driver_en_cours");

        // La file de la finance, et le releve du livreur.
        builder.HasIndex(p => new { p.Status, p.RequestedAt });
        builder.HasIndex(p => new { p.DriverId, p.RequestedAt });
    }
}
