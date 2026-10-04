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

        // AUCUN DEFAUT EN BASE POUR LA NATURE : les grilles existantes sont reprises
        // explicitement en Delivery par la migration. Un DEFAULT rendrait silencieuse
        // une insertion qui l'oublie, alors que l'agregat la refuse.
        builder.Property(t => t.ServiceType).HasColumnName("service_type").HasConversion<int>();

        // LA PLAGE HORAIRE EN DEUX COLONNES DE LA MEME LIGNE. Elle est lue avec la
        // grille, jamais seule : une table fille couterait une jointure sur le chemin
        // du devis pour ranger deux entiers.
        builder.OwnsOne(t => t.Band, plage =>
        {
            plage.Property(b => b.FromMinute).HasColumnName("band_from_minute").HasDefaultValue(0);
            plage.Property(b => b.ToMinute).HasColumnName("band_to_minute").HasDefaultValue(1440);
        });
        builder.Property(t => t.BaseFare).AsXof("base_fare");
        builder.Property(t => t.PerKilometer).AsXof("per_kilometer");
        builder.Property(t => t.PerMinute).AsXof("per_minute");
        builder.Property(t => t.MinimumFare).AsXof("minimum_fare");
        builder.Property(t => t.SurgeBasisPoints).HasColumnName("surge_basis_points");
        builder.Property(t => t.DriverShareBasisPoints).HasColumnName("driver_share_basis_points");

        // UN DEFAUT EN BASE ICI, CONTRAIREMENT A LA NATURE. La raison est l'inverse
        // de celle ecrite plus haut : une grille qui oublie la nature serait choisie
        // pour la mauvaise prestation, alors qu'une grille qui oublie la retenue
        // n'annule rien de travers — elle rend l'annulation gratuite. Les grilles
        // deja en base valent donc zero, et c'est exact : jusqu'au 2 octobre 2026
        // aucune annulation n'a rien retenu.
        builder.Property(t => t.CancellationFeeBasisPoints)
            .HasColumnName("cancellation_fee_basis_points")
            .HasDefaultValue(0);
        builder.Property(t => t.PerKilogram).AsXof("per_kilogram");
        builder.Property(t => t.IncludedWeightGrams).HasColumnName("included_weight_grams").HasDefaultValue(0);
        builder.Property(t => t.ValidFrom).HasColumnName("valid_from");
        builder.Property(t => t.ValidUntil).HasColumnName("valid_until");

        // C'EST LA LECTURE DU CHEMIN CRITIQUE : une grille, pour une zone, une NATURE,
        // un vehicule, a un instant — puis la plage horaire est triee en memoire sur
        // les deux ou trois lignes qui restent.
        //
        // LA NATURE EST EN DEUXIEME POSITION, AVANT LE VEHICULE. Une zone porte deux
        // natures et cinq vehicules : filtrer d'abord sur la nature divise par deux
        // avant de descendre, et c'est l'ordre dans lequel la requete les presente.
        //
        // PAS D'INDEX UNIQUE : plusieurs grilles coexistent legitimement sur la meme
        // cle, une par plage horaire. C'est le controle de chevauchement, cote
        // application, qui garantit qu'une minute n'a qu'un prix — une contrainte de
        // base ne sait pas exprimer « ces deux plages ne se recouvrent pas » sans une
        // extension que ce depot n'installe pas.
        builder.HasIndex(t => new { t.ZoneCode, t.ServiceType, t.VehicleType, t.ValidFrom });
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

        // LA NATURE EST FIGEE DANS LE DEVIS, PAS RELUE DANS LA GRILLE. Un devis est un
        // instantane (ADR 0004) : sa grille peut etre close, remplacee, et la relire
        // ferait dependre la verification de consommation d'une ligne qui a bouge. La
        // colonne est donc nommee explicitement, comme toutes les autres de cette
        // table — sans quoi la convention EF l'aurait appelee « ServiceType » au
        // milieu d'un schema en minuscules_soulignees.
        builder.Property(q => q.ServiceType).HasColumnName("service_type").HasConversion<int>();
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

            // LE DEFAUT « FAUX » NE REECRIT PAS L'HISTOIRE, PARCE QU'IL N'Y EN A
            // PAS. Avant le 1er octobre 2026 toutes les mesures etaient a vol
            // d'oiseau, donc en toute rigueur les anciennes lignes devraient
            // valoir vrai — mais un devis expire en quelques minutes et aucune
            // ligne de cette table ne survit a la migration. Le defaut sert aux
            // insertions, pas a la reprise.
            route.Property(r => r.IsDegraded)
                .HasColumnName("distance_is_degraded")
                .HasDefaultValue(false);
        });

        builder.Property(q => q.Total).AsXof("total");
        builder.Property(q => q.BaseFare).AsXof("base_fare");
        builder.Property(q => q.VariableFare).AsXof("variable_fare");
        builder.Property(q => q.SurgeFare).AsXof("surge_fare");
        builder.Property(q => q.DriverEarning).AsXof("driver_earning");

        // PAS DE HasDefaultValue ICI, CONTRAIREMENT AUX DEUX AUTRES COLONNES AJOUTEES
        // CE JOUR-LA. Le montant passe par un convertisseur de valeur (MoneyXof vers
        // bigint) et EF applique le convertisseur au defaut : un 0L nu s'y casserait.
        // La migration generee pose de toute facon « defaultValue: 0L » sur une
        // colonne non nullable ajoutee a une table existante, ce qui suffit — et les
        // devis anterieurs valent bien zero, aucune annulation n'ayant jamais retenu
        // quoi que ce soit avant le 2 octobre 2026.
        builder.Property(q => q.CancellationFee).AsXof("cancellation_fee");

        builder.Property(q => q.CreatedAt).HasColumnName("created_at");
        builder.Property(q => q.ExpiresAt).HasColumnName("expires_at");
        builder.Property(q => q.ConsumedAt).HasColumnName("consumed_at");
        builder.Property(q => q.ConsumedByDeliveryId).HasColumnName("consumed_by_delivery_id");

        builder.Ignore(q => q.IsConsumed);
    }
}
