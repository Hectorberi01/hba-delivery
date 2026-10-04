using Hba.Rating.Domain.Reviews;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hba.Rating.Infrastructure.Persistence.Configurations;

public sealed class ReviewConfiguration : IEntityTypeConfiguration<Review>
{
    public void Configure(EntityTypeBuilder<Review> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("reviews");

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).HasColumnName("id");

        builder.Property(r => r.MissionType).HasColumnName("mission_type").HasConversion<int>();
        builder.Property(r => r.ReferenceId).HasColumnName("reference_id");
        builder.Property(r => r.CustomerId).HasColumnName("customer_id").HasMaxLength(64).IsRequired();
        builder.Property(r => r.DriverId).HasColumnName("driver_id");

        builder.Property(r => r.Stars).HasColumnName("stars");
        builder.Property(r => r.Comment)
            .HasColumnName("comment")
            .HasMaxLength(Review.LongueurMaximaleDuCommentaire);

        builder.Property(r => r.MissionCompletedAt).HasColumnName("mission_completed_at");
        builder.Property(r => r.SubmittedAt).HasColumnName("submitted_at");

        // UNE MISSION, UNE NOTATION. C'est la garantie d'idempotence du
        // consommateur : Kafka rejoue, et sans cet index unique un evenement relu
        // ouvrirait une seconde ligne — le client verrait deux fois la meme course
        // a noter, et le livreur recevrait deux notes pour un seul trajet.
        //
        // LE TYPE EST DANS LA CLE, et ce n'est pas du zele : une course et une
        // livraison peuvent porter le meme identifiant, puisque chaque service
        // tire les siens.
        builder.HasIndex(r => new { r.MissionType, r.ReferenceId })
            .IsUnique()
            .HasDatabaseName("ix_reviews_mission");

        // CE QUE LE CLIENT PEUT ENCORE NOTER. L'ecran de fin de course le demande
        // a chaque ouverture ; sans index, la requete balaie toute la table.
        builder.HasIndex(r => new { r.CustomerId, r.SubmittedAt })
            .HasDatabaseName("ix_reviews_client_a_noter");

        // LA MOYENNE D'UN LIVREUR, calculee en base a chaque ouverture de son
        // application.
        builder.HasIndex(r => new { r.DriverId, r.Stars })
            .HasDatabaseName("ix_reviews_livreur");
    }
}
