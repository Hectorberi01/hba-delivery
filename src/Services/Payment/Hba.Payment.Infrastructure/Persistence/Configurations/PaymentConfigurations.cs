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
        // LA COLONNE EST RENOMMEE, PAS AJOUTEE : « delivery_id » devient
        // « reference_id ». La migration porte un RENAME, donc les intentions en
        // cours au moment du deploiement gardent leur mission. Une colonne neuve
        // les aurait laissees a zero — c'est-a-dire des paiements orphelins.
        builder.Property(p => p.ReferenceId).HasColumnName("reference_id");

        // AUCUN DEFAUT EN BASE : les lignes existantes sont reprises explicitement
        // en Delivery (1) par la migration. Un DEFAULT rendrait silencieuse une
        // insertion qui l'oublie, alors que l'agregat la refuse justement.
        builder.Property(p => p.ServiceType).HasColumnName("service_type").HasConversion<int>();
        builder.Property(p => p.PayerId).HasColumnName("payer_id").HasMaxLength(64).IsRequired();
        builder.Property(p => p.PayerPhone).HasColumnName("payer_phone").HasMaxLength(32);

        // Entier de francs CFA : le XOF n'a pas de subdivision (ADR 0006), et
        // FedaPay attend le meme entier.
        builder.Property(p => p.Amount)
            .HasColumnName("amount")
            .HasConversion(money => money.Amount, amount => MoneyXof.From(amount))
            .HasColumnType("bigint")
            .IsRequired();

        // LA RAISON DU PAIEMENT, AJOUTEE LE 3 OCTOBRE 2026 AVEC LE POURBOIRE.
        //
        // MISSION VAUT ZERO, DONC LES LIGNES EXISTANTES SONT DEJA JUSTES : toutes
        // reglaient une mission, et la colonne ajoutee les decrit sans qu'aucune
        // n'ait a etre reecrite.
        builder.Property(p => p.Purpose).HasColumnName("purpose").HasConversion<int>();

        // RENSEIGNE POUR UN POURBOIRE SEULEMENT. Pour une mission, le chauffeur
        // est credite par l'evenement de fin de mission, qui le porte deja.
        builder.Property(p => p.DriverId).HasColumnName("driver_id").HasMaxLength(64);

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

        // LA NATURE FAIT PARTIE DE L'INDEX. « L'intention vivante de cette
        // mission » se cherche sur les deux colonnes : un index sur l'identifiant
        // seul laisserait le filtre de nature se faire en memoire.
        builder.HasIndex(p => new { p.ServiceType, p.ReferenceId });

        // DEUX INDEX, PARCE QU'IL Y A DEUX LECTURES (ADR 0019). La cohorte se
        // lit par date d'ouverture, la recette par date d'encaissement : un
        // seul des deux laisserait l'autre balayer la table.
        builder.HasIndex(p => p.CreatedAt);
        builder.HasIndex(p => p.SucceededAt);

        builder.Ignore(p => p.IsSettled);
    }
}
