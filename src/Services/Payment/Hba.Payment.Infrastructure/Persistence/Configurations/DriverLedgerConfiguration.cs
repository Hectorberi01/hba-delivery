using Hba.Payment.Domain.Earnings;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hba.Payment.Infrastructure.Persistence.Configurations;

internal sealed class DriverLedgerEntryConfiguration : IEntityTypeConfiguration<DriverLedgerEntry>
{
    public void Configure(EntityTypeBuilder<DriverLedgerEntry> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("driver_ledger_entries");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id");
        builder.Property(e => e.DriverId).HasColumnName("driver_id").HasMaxLength(64).IsRequired();
        builder.Property(e => e.Kind).HasColumnName("kind").HasConversion<int>().IsRequired();
        builder.Property(e => e.Direction).HasColumnName("direction").HasConversion<int>().IsRequired();

        // ENTIER DE FRANCS CFA, SANS CONVERTISSEUR DE VALEUR — contrairement
        // au montant d'une intention de paiement. Le convertisseur rend la
        // propriete opaque a LINQ : « Sum(e => e.Amount.Amount) » ne se
        // traduit pas, et le cumul du compte echouerait a la lecture. Ici la
        // somme est le coeur du releve, elle doit se faire en base.
        builder.Property(e => e.AmountXof)
            .HasColumnName("amount_xof")
            .HasColumnType("bigint")
            .IsRequired();

        builder.Property(e => e.DeliveryId).HasColumnName("delivery_id");
        builder.Property(e => e.DeliveryReference).HasColumnName("delivery_reference").HasMaxLength(32);
        builder.Property(e => e.PayoutId).HasColumnName("payout_id");
        builder.Property(e => e.OccurredAt).HasColumnName("occurred_at").IsRequired();

        // PAS DE JETON DE CONCURRENCE, ET C'EST VOLONTAIRE. Une ligne de grand
        // livre ne se modifie jamais : elle est ecrite une fois, puis lue. Il
        // n'y a donc pas de seconde ecriture a departager.

        // LE RELEVE D'UN LIVREUR, DU PLUS RECENT AU PLUS ANCIEN. C'est la
        // seule lecture chaude de cette table.
        builder.HasIndex(e => new { e.DriverId, e.OccurredAt });

        // UNE COURSE NE SE PAIE QU'UNE FOIS, ET LA BASE LE GARANTIT.
        //
        // Le controle applicatif lit avant d'ecrire ; deux consommateurs
        // concurrents peuvent lire en meme temps et conclure tous deux que la
        // ligne manque. Cet index les departage — l'un echoue, et payer deux
        // fois la meme course n'arrive pas.
        //
        // FILTRE SUR LA REMUNERATION : un versement n'a pas de course, et
        // plusieurs versements ne doivent pas se gener entre eux.
        builder.HasIndex(e => e.DeliveryId)
            .IsUnique()
            .HasFilter("kind = 1 AND delivery_id IS NOT NULL");

        // UN VERSEMENT NE SE DEBITE QU'UNE FOIS, MEME SYMETRIE.
        //
        // La machine a etats de la demande interdit deja de repasser par
        // « verse » : le deuxieme appel echoue avant d'ecrire. Cet index est la
        // pour le cas que la machine a etats ne voit pas — deux requetes qui
        // chargent la meme demande approuvee en meme temps, decident toutes
        // deux que la transition est legale, et ecrivent. Le xmin de la demande
        // en arrete une ; celui-ci arrete la ligne de grand livre, qui n'a pas
        // de xmin parce qu'elle ne se modifie jamais.
        builder.HasIndex(e => e.PayoutId)
            .IsUnique()
            .HasFilter("kind = 2 AND payout_id IS NOT NULL");
    }
}
