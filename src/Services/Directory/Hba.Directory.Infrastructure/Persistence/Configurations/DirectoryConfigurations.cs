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

        builder.HasIndex(c => c.Phone).IsUnique();

        // Les adresses favorites n'existent pas hors d'un client : type possédé,
        // supprimé avec lui.
        builder.OwnsMany(c => c.FavoriteAddresses, address =>
        {
            address.ToTable("customer_favorite_addresses");
            address.WithOwner().HasForeignKey("CustomerId");
            address.HasKey(a => a.Id);

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
