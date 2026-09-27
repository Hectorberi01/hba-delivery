using Hba.Pricing.Domain.Quotes;
using Hba.Pricing.Domain.Tariffs;
using Hba.Pricing.Domain.ValueObjects;
using Hba.Pricing.Domain.Zones;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hba.Pricing.Infrastructure.Persistence.Configurations;

/// <summary>
/// Les montants sont stockes en entiers de francs CFA. LE XOF N'A PAS DE
/// SUBDIVISION : un decimal ouvrirait la porte a des demi-francs qui n'existent
/// pas, et a des arrondis qui ne tomberaient jamais deux fois pareil (ADR 0006).
/// </summary>
internal static class MoneyConversion
{
    public static PropertyBuilder<MoneyXof> AsXof(this PropertyBuilder<MoneyXof> builder, string column)
        => builder
            .HasColumnName(column)
            .HasConversion(money => money.Amount, amount => MoneyXof.From(amount))
            .HasColumnType("bigint")
            .IsRequired();
}

internal sealed class TariffConfiguration : IEntityTypeConfiguration<Tariff>
{
    public void Configure(EntityTypeBuilder<Tariff> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("tariffs");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).HasColumnName("id");
        builder.Property(t => t.TariffVersion).HasColumnName("tariff_version").HasMaxLength(64).IsRequired();
        builder.Property(t => t.ZoneCode).HasColumnName("zone_code").HasMaxLength(64).IsRequired();
        builder.Property(t => t.VehicleType).HasColumnName("vehicle_type").IsRequired();
        builder.Property(t => t.BaseFare).AsXof("base_fare");
        builder.Property(t => t.PerKilometer).AsXof("per_kilometer");
        builder.Property(t => t.PerMinute).AsXof("per_minute");
        builder.Property(t => t.MinimumFare).AsXof("minimum_fare");
        builder.Property(t => t.SurgeBasisPoints).HasColumnName("surge_basis_points");
        builder.Property(t => t.DriverShareBasisPoints).HasColumnName("driver_share_basis_points");
        builder.Property(t => t.ValidFrom).HasColumnName("valid_from");
        builder.Property(t => t.ValidUntil).HasColumnName("valid_until");

        // C'est la lecture du chemin critique : une grille, pour une zone, un
        // vehicule, a un instant.
        builder.HasIndex(t => new { t.ZoneCode, t.VehicleType, t.ValidFrom });
    }
}

internal sealed class ZoneConfiguration : IEntityTypeConfiguration<Zone>
{
    public void Configure(EntityTypeBuilder<Zone> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("zones");
        builder.HasKey(z => z.Id);
        builder.Property(z => z.Id).HasColumnName("id");
        builder.Property(z => z.Code).HasColumnName("code").HasMaxLength(64).IsRequired();
        builder.Property(z => z.Name).HasColumnName("name").HasMaxLength(128).IsRequired();
        builder.Property(z => z.Priority).HasColumnName("priority");
        builder.Property(z => z.IsActive).HasColumnName("is_active");
        builder.Property(z => z.CreatedAt).HasColumnName("created_at");

        builder.HasIndex(z => z.Code).IsUnique();

        // LE POLYGONE N'EST PAS UNE PROPRIETE DE L'AGREGAT, a dessein : le
        // domaine ne fait pas de geometrie. La colonne sera ajoutee par une
        // migration dediee le jour ou les zones seront reellement dessinees,
        // et seule la requete de recherche la lira.
    }
}

internal sealed class QuoteConfiguration : IEntityTypeConfiguration<Quote>
{
    public void Configure(EntityTypeBuilder<Quote> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("quotes");
        builder.HasKey(q => q.Id);
        builder.Property(q => q.Id).HasColumnName("id");
        builder.Property(q => q.TariffVersion).HasColumnName("tariff_version").HasMaxLength(64).IsRequired();
        builder.Property(q => q.ZoneCode).HasColumnName("zone_code").HasMaxLength(64).IsRequired();
        builder.Property(q => q.VehicleType).HasColumnName("vehicle_type");

        builder.OwnsOne(q => q.Pickup, point =>
        {
            point.Property(p => p.Latitude).HasColumnName("pickup_latitude");
            point.Property(p => p.Longitude).HasColumnName("pickup_longitude");
        });

        builder.OwnsOne(q => q.Dropoff, point =>
        {
            point.Property(p => p.Latitude).HasColumnName("dropoff_latitude");
            point.Property(p => p.Longitude).HasColumnName("dropoff_longitude");
        });

        builder.OwnsOne(q => q.Route, route =>
        {
            route.Property(r => r.DistanceMeters).HasColumnName("distance_meters");
            route.Property(r => r.DurationSeconds).HasColumnName("duration_seconds");
        });

        builder.Property(q => q.Total).AsXof("total");
        builder.Property(q => q.BaseFare).AsXof("base_fare");
        builder.Property(q => q.VariableFare).AsXof("variable_fare");
        builder.Property(q => q.SurgeFare).AsXof("surge_fare");
        builder.Property(q => q.DriverEarning).AsXof("driver_earning");

        builder.Property(q => q.CreatedAt).HasColumnName("created_at");
        builder.Property(q => q.ExpiresAt).HasColumnName("expires_at");
        builder.Property(q => q.ConsumedAt).HasColumnName("consumed_at");
        builder.Property(q => q.ConsumedByDeliveryId).HasColumnName("consumed_by_delivery_id");

        builder.Ignore(q => q.IsConsumed);
    }
}
