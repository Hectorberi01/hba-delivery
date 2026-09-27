using Hba.Dispatch.Domain.Dispatching;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hba.Dispatch.Infrastructure.Persistence.Configurations;

internal sealed class DispatchConfiguration : IEntityTypeConfiguration<DispatchAggregate>
{
    public void Configure(EntityTypeBuilder<DispatchAggregate> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("dispatches");
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).HasColumnName("id");
        builder.Property(d => d.DeliveryId).HasColumnName("delivery_id");
        builder.Property(d => d.PickupLandmark).HasColumnName("pickup_landmark").HasMaxLength(200);
        builder.Property(d => d.TripDistanceMeters).HasColumnName("trip_distance_meters");
        builder.Property(d => d.DriverEarningXof).HasColumnName("driver_earning_xof");
        builder.Property(d => d.Status).HasColumnName("status").HasConversion<int>();
        builder.Property(d => d.CurrentWave).HasColumnName("current_wave");
        builder.Property(d => d.CurrentWaveOpenedAt).HasColumnName("current_wave_opened_at");
        builder.Property(d => d.AssignedDriverId).HasColumnName("assigned_driver_id").HasMaxLength(64);
        builder.Property(d => d.CreatedAt).HasColumnName("created_at");
        builder.Property(d => d.ClosedAt).HasColumnName("closed_at");

        builder.OwnsOne(d => d.Pickup, point =>
        {
            point.Property(p => p.Latitude).HasColumnName("pickup_latitude");
            point.Property(p => p.Longitude).HasColumnName("pickup_longitude");
        });

        // LE JETON DE CONCURRENCE EST LA TROISIEME BARRIERE de l'acceptation :
        // si le verrou Redis a expire au mauvais moment, c'est lui qui refuse
        // la seconde ecriture au lieu de donner la course a deux livreurs.
        builder.Property(d => d.Version).IsRowVersion().HasColumnName("xmin").HasColumnType("xid");

        // UNE SEULE RECHERCHE PAR LIVRAISON. Le rejeu d'un evenement Kafka ne
        // doit pas en ouvrir une seconde, et la contrainte le garantit meme si
        // le controle applicatif etait contourne.
        builder.HasIndex(d => d.DeliveryId).IsUnique();

        // LA REQUETE DU PLANIFICATEUR : les recherches encore ouvertes.
        builder.HasIndex(d => new { d.Status, d.CurrentWaveOpenedAt });

        // LES LECTURES AGREGEES PARTENT D'UNE PLAGE DE DATES (ADR 0019).
        // L'index ci-dessus commence par le statut : il ne sert a rien pour
        // « toutes les recherches du mois, tous statuts confondus ».
        builder.HasIndex(d => d.CreatedAt);

        builder.Ignore(d => d.IsOpen);
        builder.Ignore(d => d.SolicitedDriverIds);

        builder.OwnsMany(d => d.Offers, offer =>
        {
            offer.ToTable("offers");
            offer.WithOwner().HasForeignKey("dispatch_id");
            offer.HasKey(o => o.Id);
            // CETTE LIGNE EST CE QUI FAIT INSERER L'OFFRE, et elle n'est pas
            // decorative.
            //
            // PAR CONVENTION, EF REND UNE CLE Guid « ValueGeneratedOnAdd ».
            // Quand il decouvre ensuite un enfant dans une collection possedee
            // dont le proprietaire est deja en base, il tranche « neuf ou
            // existant ? » en croisant deux choses : la cle est-elle
            // renseignee, et est-elle censee venir du magasin ? Les deux oui
            // ensemble lui font conclure que la ligne existe : UPDATE sur une
            // ligne jamais ecrite, zero ligne affectee,
            // DbUpdateConcurrencyException. Le journal SQL du 27 septembre
            // 2026 l'a montre sur les pieces du dossier livreur.
            //
            // ValueGeneratedNever CASSE LE CROISEMENT : la cle est posee, mais
            // le magasin n'y est pour rien, donc l'enfant est neuf. Et le
            // domaine garde son identifiant des la construction — ce dont
            // OfferSent a besoin, puisque l'evenement est leve avant
            // SaveChanges.
            //
            // CE N'EST PAS LA MEME FORME QUE POUR LES PIECES DU DOSSIER
            // LIVREUR, ou un generateur EF a ete pose. La difference est que
            // DriverDocumentAttached ne transporte pas l'identifiant de la
            // piece, seulement son type et sa cle de stockage : une cle posee
            // au SaveChanges y suffit. Ici elle ne suffirait pas.
            offer.Property(o => o.Id)
                .HasColumnName("id")
                .ValueGeneratedNever();
            offer.Property(o => o.DriverId).HasColumnName("driver_id").HasMaxLength(64).IsRequired();
            offer.Property(o => o.Status).HasColumnName("status").HasConversion<int>();
            offer.Property(o => o.WaveNumber).HasColumnName("wave_number");
            offer.Property(o => o.DistanceToPickupMeters).HasColumnName("distance_to_pickup_meters");
            offer.Property(o => o.SentAt).HasColumnName("sent_at");
            offer.Property(o => o.ExpiresAt).HasColumnName("expires_at");
            offer.Property(o => o.ResolvedAt).HasColumnName("resolved_at");
            offer.Property(o => o.DeclineReason).HasColumnName("decline_reason").HasMaxLength(300);

            offer.Ignore(o => o.IsPending);

            // « Quelle est l'offre en cours de ce livreur » et « quelles offres
            // ont expire » sont les deux lectures chaudes.
            offer.HasIndex(o => new { o.DriverId, o.Status });
            offer.HasIndex(o => new { o.Status, o.ExpiresAt });

            // Meme raison : les offres se comptent par date d'envoi.
            offer.HasIndex(o => o.SentAt);
        });
    }
}
