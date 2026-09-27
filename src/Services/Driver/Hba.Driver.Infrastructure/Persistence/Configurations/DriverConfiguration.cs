using Hba.Driver.Domain.Drivers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hba.Driver.Infrastructure.Persistence.Configurations;

internal sealed class DriverConfiguration : IEntityTypeConfiguration<DriverAggregate>
{
    public void Configure(EntityTypeBuilder<DriverAggregate> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("drivers");
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).HasColumnName("id");
        builder.Property(d => d.DisplayName).HasColumnName("display_name").HasMaxLength(120).IsRequired();
        builder.Property(d => d.Phone).HasColumnName("phone").HasMaxLength(20).IsRequired();
        builder.Property(d => d.VerificationStatus).HasColumnName("verification_status").HasConversion<int>();
        builder.Property(d => d.OperationalStatus).HasColumnName("operational_status").HasConversion<int>();
        builder.Property(d => d.StatusReason).HasColumnName("status_reason").HasMaxLength(500);
        builder.Property(d => d.RegisteredAt).HasColumnName("registered_at");
        builder.Property(d => d.VerifiedAt).HasColumnName("verified_at");
        builder.Property(d => d.ProfilePhotoKey).HasColumnName("profile_photo_key").HasMaxLength(512);
        builder.Property(d => d.SubmittedAt).HasColumnName("submitted_at");

        builder.OwnsOne(d => d.Vehicle, vehicle =>
        {
            vehicle.Property(v => v.Type).HasColumnName("vehicle_type").HasConversion<int>();
            vehicle.Property(v => v.Plate).HasColumnName("vehicle_plate").HasMaxLength(32);
            vehicle.Property(v => v.CapacityGrams).HasColumnName("vehicle_capacity_grams");
        });

        builder.Property(d => d.Version).IsRowVersion().HasColumnName("xmin").HasColumnType("xid");

        builder.HasIndex(d => d.Phone).IsUnique();

        // LA LECTURE DU CHEMIN CRITIQUE DU DISPATCH : parmi une poignee
        // d'identifiants rendus par Redis, lesquels sont verifies ET
        // disponibles. L'index porte les deux colonnes du filtre.
        builder.HasIndex(d => new { d.VerificationStatus, d.OperationalStatus });

        // LES FLUX SE LISENT PAR DATE (ADR 0019) : inscriptions et
        // validations. Les effectifs, eux, balaient la table entiere — c'est
        // voulu et sans consequence : un GROUP BY sur quelques centaines de
        // lignes ne gagne rien a un index sur une colonne a quatre valeurs.
        builder.HasIndex(d => d.RegisteredAt);
        builder.HasIndex(d => d.VerifiedAt);

        // LES PIECES SONT DANS L'AGREGAT, DONC DANS SA TRANSACTION. Une table
        // a part avec son propre depot laisserait exister une piece sans
        // dossier, ou un dossier soumis a qui il manque une piece qu'on croit
        // deposee. La cascade suit : supprimer un livreur emporte ses lignes
        // de pieces — les objets, eux, se suppriment par leur prefixe.
        builder.OwnsMany<DriverDocument>("_documents", documents =>
        {
            documents.ToTable("driver_documents");
            documents.WithOwner().HasForeignKey("driver_id");
            documents.HasKey(d => d.Id);

            // LA CLE EST POSEE ICI, PAS DANS LE DOMAINE. DriverDocument.Create
            // laisse l'identifiant vide expres : c'est ce qui fait qu'EF
            // reconnait une piece neuve et l'INSERE au lieu de tenter un
            // UPDATE sur une ligne inexistante. Le generateur rend le meme
            // GUID v7 qu'avant.
            documents.Property(d => d.Id)
                .HasColumnName("id")
                .ValueGeneratedOnAdd()
                .HasValueGenerator<GuidVersion7Generator>();
            documents.Property(d => d.Type).HasColumnName("type").HasConversion<int>();
            documents.Property(d => d.ObjectKey).HasColumnName("object_key").HasMaxLength(512).IsRequired();
            documents.Property(d => d.ContentType).HasColumnName("content_type").HasMaxLength(100).IsRequired();
            documents.Property(d => d.SizeBytes).HasColumnName("size_bytes");
            documents.Property(d => d.UploadedAt).HasColumnName("uploaded_at");

            // UNE SEULE PIECE PAR NATURE ET PAR LIVREUR. L'agregat le fait
            // deja respecter en memoire ; l'index l'impose a la base, qui est
            // la seule a trancher si deux depots arrivent ensemble.
            //
            // HasIndex PREND DES NOMS DE PROPRIETES, PAS DE COLONNES. Ecrire
            // « type » au lieu de « Type » fait chercher a EF une propriete
            // fantome et casse la creation du DbContext au design-time, avec
            // un message qui parle de shadow property et jamais d'index.
            // « driver_id » en revanche est bien une propriete fantome : c'est
            // la cle etrangere declaree juste au-dessus.
            documents.HasIndex("driver_id", nameof(DriverDocument.Type)).IsUnique();
        });

        builder.Navigation("_documents").UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Ignore(d => d.CanWork);
        builder.Ignore(d => d.Documents);
        builder.Ignore(d => d.PiecesManquantes);
        builder.Ignore(d => d.DossierModifiable);
    }
}
