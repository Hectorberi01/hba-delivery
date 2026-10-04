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

        // RENOMMEES, PAS AJOUTEES : les lignes deja ecrites gardent leur mission.
        builder.Property(e => e.ReferenceId).HasColumnName("reference_id");
        builder.Property(e => e.ReferenceLabel).HasColumnName("reference_label").HasMaxLength(64);

        // NON PRECISEE POUR UN VERSEMENT, reprise en Delivery (1) par la migration
        // pour toutes les remunerations deja inscrites.
        builder.Property(e => e.ServiceType).HasColumnName("service_type").HasConversion<int>();
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
        // FILTRE SUR LA REMUNERATION : un versement n'a pas de mission, et
        // plusieurs versements ne doivent pas se gener entre eux.
        //
        // LA NATURE EST DANS LA CLE, ET SANS ELLE L'INDEX SERAIT FAUX : une
        // livraison et une course peuvent porter le meme identifiant — rien ne les
        // tire du meme generateur — et le second credit serait rejete comme un
        // doublon. Un chauffeur perdrait l'argent d'une course sans que rien ne le
        // signale.
        builder.HasIndex(e => new { e.ServiceType, e.ReferenceId })
            .IsUnique()
            .HasDatabaseName("ux_driver_ledger_mission")
            .HasFilter("kind = 1 AND reference_id IS NOT NULL");

        // UN DEDOMMAGEMENT NON PLUS, ET IL LUI FAUT SON PROPRE INDEX.
        //
        // Celui du dessus filtre sur « kind = 1 » : il ne voit pas les lignes de
        // genre 3 et ne les protege donc pas. Un rejeu de « DeliveryCancelled »
        // apres une purge de l'Inbox crediterait le livreur une seconde fois sans
        // que rien ne l'arrete.
        //
        // DEUX INDEX PLUTOT QU'UN SEUL SANS FILTRE DE GENRE, et c'est delibere :
        // un index unique sur (nature, mission) toutes lignes confondues
        // interdirait a une meme mission de porter une remuneration ET un
        // dedommagement. Les deux ne coexistent pas aujourd'hui, mais rien dans
        // cette table ne le garantit, et un index n'est pas l'endroit ou inscrire
        // une regle metier qu'on n'a pas ecrite.
        builder.HasIndex(e => new { e.ServiceType, e.ReferenceId })
            .IsUnique()
            .HasDatabaseName("ux_driver_ledger_compensation")
            .HasFilter("kind = 3 AND reference_id IS NOT NULL");

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
