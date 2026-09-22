using System.Linq.Expressions;
using Hba.Delivery.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hba.Delivery.Infrastructure.Persistence.Configurations;

/// <summary>
/// Cartographie de l'agrégat. Les objets-valeurs sont des types possédés
/// (OwnsOne) et non des types complexes. Ceux qui ne contiennent que des
/// scalaires sont reconstruits par leur constructeur privé ; ceux qui en
/// contiennent un autre (Location porte un GeoPoint, PricingSnapshot des
/// MoneyXof) ont un constructeur sans paramètre, car EF ne peut pas passer une
/// navigation à un paramètre de constructeur. L'immutabilité tient dans les
/// deux cas : seul Create est public. Tout est aplati dans la table deliveries.
/// </summary>
internal sealed class DeliveryConfiguration : IEntityTypeConfiguration<DeliveryAggregate>
{
    public void Configure(EntityTypeBuilder<DeliveryAggregate> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("deliveries");
        builder.HasKey(d => d.Id);

        builder.Ignore(d => d.DomainEvents);

        // Concurrence optimiste native PostgreSQL : deux affectations simultanées
        // ne peuvent pas réussir toutes les deux.
        builder.Property(d => d.Version)
            .IsRowVersion()
            .HasColumnName("xmin")
            .HasColumnType("xid");

        builder.Property(d => d.Reference).HasMaxLength(32).IsRequired();
        builder.HasIndex(d => d.Reference).IsUnique();

        builder.Property(d => d.Status).HasConversion<int>().IsRequired();
        builder.Property(d => d.Source).HasConversion<int>().IsRequired();

        builder.Property(d => d.PartnerId).HasMaxLength(64).IsRequired();
        builder.Property(d => d.ExternalOrderId).HasMaxLength(128);
        builder.Property(d => d.CustomerId).HasMaxLength(64);
        builder.Property(d => d.MerchantId).HasMaxLength(64);
        builder.Property(d => d.PickupPointId).HasMaxLength(64);
        builder.Property(d => d.PaymentIntentId).HasMaxLength(64);
        builder.Property(d => d.CurrentOfferId).HasMaxLength(64);
        builder.Property(d => d.PackageDescription).HasMaxLength(500);
        builder.Property(d => d.PickupProofObjectKey).HasMaxLength(256);
        builder.Property(d => d.DeliveryProofObjectKey).HasMaxLength(256);
        builder.Property(d => d.ClosureReason).HasMaxLength(500);

        // Un partenaire ne peut pas créer deux fois la même commande externe.
        builder.HasIndex(d => new { d.PartnerId, d.ExternalOrderId })
            .IsUnique()
            .HasFilter("\"ExternalOrderId\" IS NOT NULL");

        builder.HasIndex(d => new { d.CustomerId, d.CreatedAt });
        builder.HasIndex(d => new { d.MerchantId, d.CreatedAt });
        builder.HasIndex(d => d.Status);

        ConfigureLocation(builder, d => d.Pickup, "pickup");
        ConfigureLocation(builder, d => d.Dropoff, "dropoff");
        builder.Navigation(d => d.Pickup).IsRequired();
        builder.Navigation(d => d.Dropoff).IsRequired();

        builder.OwnsOne(d => d.Recipient, recipient =>
        {
            recipient.Property(r => r.Name).HasColumnName("recipient_name").HasMaxLength(120).IsRequired();
            recipient.Property(r => r.Phone).HasColumnName("recipient_phone").HasMaxLength(20).IsRequired();
        });
        builder.Navigation(d => d.Recipient).IsRequired();

        builder.OwnsOne(d => d.Otp, otp =>
        {
            otp.Property(o => o.Code).HasColumnName("otp_code").HasMaxLength(10).IsRequired();
            otp.Property(o => o.FailedAttempts).HasColumnName("otp_failed_attempts");
            otp.Ignore(o => o.IsLocked);
        });
        builder.Navigation(d => d.Otp).IsRequired();

        // Nul tant qu'aucun livreur n'est affecté : navigation facultative.
        builder.OwnsOne(d => d.Driver, driver =>
        {
            driver.Property(x => x.DriverId).HasColumnName("driver_id").HasMaxLength(64);
            driver.Property(x => x.DisplayName).HasColumnName("driver_name").HasMaxLength(120);
            driver.Property(x => x.Phone).HasColumnName("driver_phone").HasMaxLength(20);
            driver.Property(x => x.VehicleType).HasColumnName("driver_vehicle_type").HasConversion<int>();
            driver.Property(x => x.VehiclePlate).HasColumnName("driver_vehicle_plate").HasMaxLength(20);
        });

        builder.OwnsOne(d => d.Pricing, pricing =>
        {
            pricing.Property(p => p.QuoteId).HasColumnName("quote_id").HasMaxLength(64).IsRequired();
            pricing.Property(p => p.TariffVersion).HasColumnName("tariff_version").HasMaxLength(32).IsRequired();
            pricing.Property(p => p.DistanceMeters).HasColumnName("distance_meters");
            pricing.Property(p => p.DurationSeconds).HasColumnName("duration_seconds");
            pricing.Property(p => p.QuotedAt).HasColumnName("quoted_at");

            // Le XOF n'a pas de décimale : bigint, jamais numeric.
            OwnMoney(pricing, p => p.Total, "price_total_xof");
            OwnMoney(pricing, p => p.BaseFare, "price_base_xof");
            OwnMoney(pricing, p => p.DistanceFare, "price_distance_xof");
            OwnMoney(pricing, p => p.SurgeFare, "price_surge_xof");
            OwnMoney(pricing, p => p.DriverEarning, "driver_earning_xof");
        });
        builder.Navigation(d => d.Pricing).IsRequired();
    }

    private static void OwnMoney(
        OwnedNavigationBuilder<DeliveryAggregate, PricingSnapshot> pricing,
        Expression<Func<PricingSnapshot, MoneyXof>> selector,
        string columnName)
    {
        pricing.OwnsOne(selector!, money => money.Property(m => m.Amount).HasColumnName(columnName).IsRequired());
    }

    private static void ConfigureLocation(
        EntityTypeBuilder<DeliveryAggregate> builder,
        Expression<Func<DeliveryAggregate, Location>> selector,
        string prefix)
    {
        builder.OwnsOne(selector!, location =>
        {
            location.Property(l => l.Landmark).HasColumnName($"{prefix}_landmark").HasMaxLength(300).IsRequired();
            location.Property(l => l.Phone).HasColumnName($"{prefix}_phone").HasMaxLength(20).IsRequired();
            location.Property(l => l.ContactName).HasColumnName($"{prefix}_contact_name").HasMaxLength(120).IsRequired();
            location.Property(l => l.Notes).HasColumnName($"{prefix}_notes").HasMaxLength(500);

            location.OwnsOne(l => l.Point, point =>
            {
                point.Property(p => p.Latitude).HasColumnName($"{prefix}_latitude").IsRequired();
                point.Property(p => p.Longitude).HasColumnName($"{prefix}_longitude").IsRequired();
            });
        });
    }
}
