using Hba.Directory.Domain.Customers;
using Hba.Directory.Domain.Merchants;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hba.Directory.Infrastructure.Persistence.Configurations;

internal sealed class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("customers");
        builder.HasKey(c => c.Id);

        builder.Ignore(c => c.DomainEvents);

        builder.Property(c => c.Version).IsRowVersion().HasColumnName("xmin").HasColumnType("xid");

        builder.Property(c => c.DisplayName).HasColumnName("display_name").HasMaxLength(120).IsRequired();
        builder.Property(c => c.Phone).HasColumnName("phone").HasMaxLength(20).IsRequired();
        builder.Property(c => c.Email).HasColumnName("email").HasMaxLength(200);

        // PAS D'INDEX, ET PAS DE CLE ETRANGERE. La colonne ne sert jamais de
        // critere de recherche — on part toujours du client pour aller vers sa
        // photo, jamais l'inverse — et la table des medias vit dans une AUTRE
        // base : une contrainte referentielle y est impossible, ce qui est le
        // prix assume du decoupage (point 27).
        builder.Property(c => c.PhotoMediaId).HasColumnName("photo_media_id");

        builder.HasIndex(c => c.Phone).IsUnique();

        // Les adresses favorites n'existent pas hors d'un client : type possédé,
        // supprimé avec lui.
        builder.OwnsMany(c => c.FavoriteAddresses, address =>
        {
            address.ToTable("customer_favorite_addresses");
            address.WithOwner().HasForeignKey("CustomerId");
            address.HasKey(a => a.Id);

            // CETTE LIGNE EST CE QUI FAIT INSERER UNE ADRESSE FAVORITE, et elle n'est pas
            // decorative.
            //
            // PAR CONVENTION, EF REND UNE CLE Guid « ValueGeneratedOnAdd ».
            // Quand il decouvre un enfant dans une collection possedee dont le
            // proprietaire est DEJA EN BASE, il tranche « neuf ou existant ? »
            // en croisant deux choses : la cle est-elle renseignee, et est-elle
            // censee venir du magasin ? Les deux oui ensemble lui font conclure
            // que la ligne existe : UPDATE de toutes les colonnes sur une ligne
            // jamais ecrite, zero ligne affectee, DbUpdateConcurrencyException,
            // et un 500 a l'ecran.
            //
            // LA MEME PANNE A ETE CORRIGEE DEUX FOIS AILLEURS LE 27 SEPTEMBRE
            // 2026 — offer.Id dans Dispatch, documents.Id dans Driver — et
            // Directory a ete oublie. Elle ne se declenche que lorsque le
            // proprietaire est deja persiste, ce qui est exactement pourquoi
            // personne ne l'a vue : la toute premiere adresse d'un client
            // fraichement cree passait, le reste non.
            //
            // ValueGeneratedNever CASSE LE CROISEMENT : la cle est posee par le
            // domaine, mais le magasin n'y est pour rien, donc l'enfant est
            // neuf. On garde cette forme plutot que le generateur EF de Driver
            // parce que l'identifiant sert a l'appelant : « PUT » et
            // « DELETE /addresses/{id} » le reprennent tel quel.
            address.Property(a => a.Id).ValueGeneratedNever();


            address.Property(a => a.Label).HasColumnName("label").HasMaxLength(60).IsRequired();
            address.Property(a => a.IsDefault).HasColumnName("is_default");
            address.Property(a => a.CreatedAt).HasColumnName("created_at");

            address.OwnsOne(a => a.Address, location =>
            {
                location.Property(l => l.Landmark).HasColumnName("landmark").HasMaxLength(300).IsRequired();
                location.Property(l => l.Phone).HasColumnName("phone").HasMaxLength(20).IsRequired();
                location.Property(l => l.ContactName).HasColumnName("contact_name").HasMaxLength(120).IsRequired();
                location.Property(l => l.Notes).HasColumnName("notes").HasMaxLength(500);

                location.OwnsOne(l => l.Point, point =>
                {
                    point.Property(p => p.Latitude).HasColumnName("latitude").IsRequired();
                    point.Property(p => p.Longitude).HasColumnName("longitude").IsRequired();
                });
            });
        });

        // EF écrit dans le champ privé : la propriété n'a pas d'accesseur en
        // écriture, et c'est voulu. Les types possédés sont chargés avec leur
        // propriétaire, sans avoir à le demander.
        builder.Navigation(c => c.FavoriteAddresses).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class MerchantConfiguration : IEntityTypeConfiguration<Merchant>
{
    public void Configure(EntityTypeBuilder<Merchant> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("merchants");
        builder.HasKey(m => m.Id);

        builder.Ignore(m => m.DomainEvents);

        builder.Property(m => m.Version).IsRowVersion().HasColumnName("xmin").HasColumnType("xid");

        builder.Property(m => m.LegalName).HasColumnName("legal_name").HasMaxLength(200).IsRequired();
        builder.Property(m => m.ContactName).HasColumnName("contact_name").HasMaxLength(120).IsRequired();
        builder.Property(m => m.ContactPhone).HasColumnName("contact_phone").HasMaxLength(20).IsRequired();
        builder.Property(m => m.ContactEmail).HasColumnName("contact_email").HasMaxLength(200);
        builder.Property(m => m.AveragePreparationMinutes).HasColumnName("average_preparation_minutes");
        builder.Property(m => m.IsActive).HasColumnName("is_active");
        builder.Property(m => m.DeactivationReason).HasColumnName("deactivation_reason").HasMaxLength(500);

        builder.HasIndex(m => m.LegalName);
        builder.HasIndex(m => m.IsActive);

        builder.OwnsMany(m => m.PickupPoints, point =>
        {
            point.ToTable("merchant_pickup_points");
            point.WithOwner().HasForeignKey("MerchantId");
            point.HasKey(p => p.Id);

            // MEME CORRECTIF, MEME RAISON, ET IL N'ETAIT PAS ENCORE TOMBE.
            // Ajouter un point de collecte a un commercant DEJA EN BASE
            // echouait a l'identique ; seul le premier point, pose pendant la
            // creation du commercant — ou tout le graphe est « Added » —
            // fonctionnait. Voir le commentaire des adresses favorites,
            // au-dessus.
            point.Property(p => p.Id).ValueGeneratedNever();


            point.Property(p => p.Name).HasColumnName("name").HasMaxLength(120).IsRequired();
            point.Property(p => p.IsActive).HasColumnName("is_active");
            point.Property(p => p.CreatedAt).HasColumnName("created_at");

            // Horaires en une colonne : « 1:480-1200;2:480-1200 ».
            point.Property<string>("OpeningHoursRaw")
                .HasColumnName("opening_hours")
                .HasMaxLength(400)
                .IsRequired();

            point.Ignore(p => p.OpeningHours);

            point.OwnsOne(p => p.Address, location =>
            {
                location.Property(l => l.Landmark).HasColumnName("landmark").HasMaxLength(300).IsRequired();
                location.Property(l => l.Phone).HasColumnName("phone").HasMaxLength(20).IsRequired();
                location.Property(l => l.ContactName).HasColumnName("contact_name").HasMaxLength(120).IsRequired();
                location.Property(l => l.Notes).HasColumnName("notes").HasMaxLength(500);

                location.OwnsOne(l => l.Point, geo =>
                {
                    geo.Property(g => g.Latitude).HasColumnName("latitude").IsRequired();
                    geo.Property(g => g.Longitude).HasColumnName("longitude").IsRequired();
                });
            });
        });

        builder.Navigation(m => m.PickupPoints).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
