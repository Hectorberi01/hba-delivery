using Hba.Media.Domain.Assets;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hba.Media.Infrastructure.Persistence;

internal sealed class MediaAssetConfiguration : IEntityTypeConfiguration<MediaAsset>
{
    public void Configure(EntityTypeBuilder<MediaAsset> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("assets");
        builder.HasKey(a => a.Id);

        // LES ENUMS SONT STOCKEES EN TEXTE, PAS EN ENTIER. Une base qu'on
        // inspecte pour comprendre une panne doit se lire sans la table de
        // correspondance du code — et reordonner une enum ne doit pas
        // transformer des cartes d'identite en photos de profil.
        builder.Property(a => a.OwnerType)
            .HasColumnName("owner_type")
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(a => a.OwnerId).HasColumnName("owner_id").HasMaxLength(64).IsRequired();

        builder.Property(a => a.Kind)
            .HasColumnName("kind")
            .HasConversion<string>()
            .HasMaxLength(48)
            .IsRequired();

        builder.Property(a => a.StorageKey).HasColumnName("storage_key").HasMaxLength(512).IsRequired();
        builder.Property(a => a.ContentType).HasColumnName("content_type").HasMaxLength(128).IsRequired();
        builder.Property(a => a.SizeBytes).HasColumnName("size_bytes").IsRequired();
        builder.Property(a => a.UploadedBy).HasColumnName("uploaded_by").HasMaxLength(128).IsRequired();
        builder.Property(a => a.CreatedAt).HasColumnName("created_at").IsRequired();

        builder.Ignore(a => a.DomainEvents);

        // L'INDEX QUI SERT A LA SUPPRESSION DE COMPTE. Enumerer ce qui
        // appartient a quelqu'un est l'operation la plus frequente du service
        // apres la lecture, et la seule qui doive rester rapide quand la table
        // aura grossi.
        builder.HasIndex(a => new { a.OwnerType, a.OwnerId, a.Kind })
            .HasDatabaseName("ix_assets_owner");

        // UNE CLE DE STOCKAGE N'APPARTIENT QU'A UNE LIGNE. Sans cette
        // contrainte, deux lignes pourraient designer le meme objet, et
        // supprimer l'une laisserait l'autre pointer dans le vide.
        builder.HasIndex(a => a.StorageKey).IsUnique().HasDatabaseName("ix_assets_storage_key");
    }
}

internal sealed class MediaAccessRecordConfiguration : IEntityTypeConfiguration<MediaAccessRecord>
{
    public void Configure(EntityTypeBuilder<MediaAccessRecord> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("access_records");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.MediaId).HasColumnName("media_id").IsRequired();
        builder.Property(r => r.RequestedBy).HasColumnName("requested_by").HasMaxLength(128).IsRequired();
        builder.Property(r => r.Reason).HasColumnName("reason").HasMaxLength(200).IsRequired();
        builder.Property(r => r.RequestedAt).HasColumnName("requested_at").IsRequired();

        builder.HasIndex(r => new { r.MediaId, r.RequestedAt })
            .HasDatabaseName("ix_access_records_media");

        // AUCUNE CLE ETRANGERE VERS LES MEDIAS, ET C'EST VOULU. Le journal doit
        // SURVIVRE a la suppression du fichier : savoir qui a consulte une
        // piece d'identite garde son interet apres l'effacement de la piece —
        // c'est meme le moment ou la question se pose.
    }
}
