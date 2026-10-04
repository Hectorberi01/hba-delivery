using Hba.Ride.Domain.Rides;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hba.Ride.Infrastructure.Persistence.Configurations;

internal sealed class RideConfiguration : IEntityTypeConfiguration<RideAggregate>
{
    /// <summary>
    /// Les états non terminaux. Écrits une fois ici pour servir à la fois de
    /// filtre d'index et de filtre de requête — deux listes finiraient par
    /// diverger, et l'index cesserait silencieusement de couvrir la requête.
    /// </summary>
    internal static readonly RideStatus[] EtatsEnCours =
    [
        RideStatus.Requested,
        RideStatus.SearchingDriver,
        RideStatus.DriverAssigned,
        RideStatus.DriverArriving,
        RideStatus.DriverArrived,
        RideStatus.InProgress,
    ];

    public void Configure(EntityTypeBuilder<RideAggregate> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("rides");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).HasColumnName("id");

        builder.Property(r => r.CustomerId).HasColumnName("customer_id").HasMaxLength(64).IsRequired();
        builder.Property(r => r.DriverId).HasColumnName("driver_id").HasMaxLength(64);
        builder.Property(r => r.VehicleType).HasColumnName("vehicle_type").HasConversion<int>();
        builder.Property(r => r.Status).HasColumnName("status").HasConversion<int>();

        builder.OwnsOne(r => r.Pickup, point =>
        {
            point.Property(p => p.Latitude).HasColumnName("pickup_latitude");
            point.Property(p => p.Longitude).HasColumnName("pickup_longitude");
        });

        builder.OwnsOne(r => r.Destination, point =>
        {
            point.Property(p => p.Latitude).HasColumnName("destination_latitude");
            point.Property(p => p.Longitude).HasColumnName("destination_longitude");
        });

        // L'ESTIMATION EST FIGÉE, DONC STOCKÉE, PAS RECALCULÉE (ADR 0004). Une
        // grille modifiée demain ne doit pas changer le prix d'une course d'hier.
        builder.OwnsOne(r => r.Estimate, estimate =>
        {
            estimate.Property(e => e.DistanceMeters).HasColumnName("estimated_distance_meters");
            estimate.Property(e => e.DurationSeconds).HasColumnName("estimated_duration_seconds");

            estimate.OwnsOne(e => e.Price, price =>
                price.Property(p => p.Amount).HasColumnName("estimated_price"));
        });

        // LA PART DU CHAUFFEUR EST STOCKÉE, PAS RECALCULÉE : la grille qui l'a
        // produite peut changer demain, et ce qui a été promis au chauffeur ne
        // change pas (ADR 0004).
        builder.OwnsOne(r => r.DriverEarning, price =>
            price.Property(p => p.Amount).HasColumnName("driver_earning"));

        // LA RETENUE D'ANNULATION, FIGEE AVEC LE RESTE DU DEVIS, et stockee pour la
        // meme raison que la part du chauffeur : la grille qui l'a produite peut
        // changer demain. Les courses anterieures au 2 octobre 2026 valent zero, ce
        // qui est exact — aucune annulation n'y retenait quoi que ce soit.
        builder.OwnsOne(r => r.CancellationFee, price =>
            price.Property(p => p.Amount).HasColumnName("cancellation_fee"));

        // NULL TANT QUE LA COURSE N'EST PAS TERMINÉE, et le montant entier est
        // donc nullable en base. Zéro ne conviendrait pas : une course gratuite
        // et une course en cours seraient indistinguables.
        builder.OwnsOne(r => r.FinalPrice, price =>
            price.Property(p => p.Amount).HasColumnName("final_price"));

        builder.Property(r => r.RequestedAt).HasColumnName("requested_at");
        builder.Property(r => r.DriverAssignedAt).HasColumnName("driver_assigned_at");
        builder.Property(r => r.DriverArrivedAt).HasColumnName("driver_arrived_at");
        builder.Property(r => r.StartedAt).HasColumnName("started_at");
        builder.Property(r => r.CompletedAt).HasColumnName("completed_at");
        builder.Property(r => r.CancelledAt).HasColumnName("cancelled_at");
        builder.Property(r => r.CancellationReason).HasColumnName("cancellation_reason").HasMaxLength(500);

        // L'INTENTION DE PAIEMENT, POUR LA TRAÇABILITÉ. L'adresse de paiement n'est
        // pas stockée : elle expire, et une adresse périmée en base finirait tôt ou
        // tard affichée dans un écran de suivi.
        builder.Property(r => r.PaymentIntentId).HasColumnName("payment_intent_id").HasMaxLength(64);

        // L'INSTANTANE DU CHAUFFEUR, EN COLONNES DE LA MEME LIGNE. Une table fille
        // couterait une jointure sur le chemin le plus chaud du service — l'ecran de
        // suivi du passager — pour ranger six valeurs qui ne sont jamais interrogees
        // seules. C'est aussi ce que fait Delivery.
        builder.OwnsOne(r => r.Driver, chauffeur =>
        {
            chauffeur.Property(d => d.DriverId).HasColumnName("driver_display_id").HasMaxLength(64);
            chauffeur.Property(d => d.DisplayName).HasColumnName("driver_display_name").HasMaxLength(120);
            chauffeur.Property(d => d.Phone).HasColumnName("driver_phone").HasMaxLength(20);
            chauffeur.Property(d => d.VehicleType).HasColumnName("driver_vehicle_type").HasConversion<int>();
            chauffeur.Property(d => d.VehiclePlate).HasColumnName("driver_vehicle_plate").HasMaxLength(32);

            // DECLARATIFS ET FACULTATIFS : ils servent au passager a reconnaitre le
            // vehicule qui arrive, pas a autoriser quoi que ce soit.
            chauffeur.Property(d => d.VehicleBrand).HasColumnName("driver_vehicle_brand").HasMaxLength(64);
            chauffeur.Property(d => d.VehicleModel).HasColumnName("driver_vehicle_model").HasMaxLength(64);
            chauffeur.Property(d => d.VehicleColor).HasColumnName("driver_vehicle_color").HasMaxLength(32);

            // « Vehicule() » EST UNE METHODE, DONC INVISIBLE D'EF : rien a ignorer.
            // C'est volontaire — une propriete calculee aurait demande un Ignore, et
            // une colonne de plus pourrait un jour contredire les trois champs.
        });

        builder.Property(r => r.Version).IsRowVersion().HasColumnName("xmin").HasColumnType("xid");

        // L'INDEX PARTIEL EST LE CHEMIN CHAUD : « les courses en cours de ce
        // client », appelé à chaque ouverture de l'application. Partiel parce que
        // l'historique grossit sans fin alors que les courses ouvertes se
        // comptent sur une main — un index complet ferait grandir l'écriture pour
        // une lecture qui ne regarde jamais le passé.
        builder.HasIndex(r => new { r.CustomerId, r.Status })
            .HasDatabaseName("ix_rides_customer_en_cours")
            .HasFilter($"status = ANY (ARRAY[{string.Join(", ", EtatsEnCours.Select(s => (int)s))}])");

        // MÊME CHOSE CÔTÉ CHAUFFEUR, interrogé au réveil de son application.
        builder.HasIndex(r => new { r.DriverId, r.Status })
            .HasDatabaseName("ix_rides_chauffeur_en_cours")
            .HasFilter($"driver_id IS NOT NULL AND status = ANY (ARRAY[{string.Join(", ", EtatsEnCours.Select(s => (int)s))}])");

        // L'HISTORIQUE SE LIT PAR DATE DÉCROISSANTE (ADR 0019).
        builder.HasIndex(r => new { r.CustomerId, r.RequestedAt });
    }
}
