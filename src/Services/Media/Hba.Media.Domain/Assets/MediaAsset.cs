using Hba.BuildingBlocks.Domain;

namespace Hba.Media.Domain.Assets;

/// <summary>
/// Un fichier déposé : ce que Media sait de lui, sans les octets.
///
/// L'INVENTAIRE EST LA RAISON D'ÊTRE DU SERVICE. Un stockage objet sait rendre
/// un fichier dont on connaît la clé ; il ne sait pas dire ce qu'il contient, à
/// qui cela appartient, ni ce qui devrait être effacé. Tant que les clés
/// vivaient éparpillées dans les bases des services, personne ne pouvait
/// répondre à « qu'est-ce qui appartient à ce compte ? » — et une suppression
/// de compte laissait derrière elle des pièces d'identité que plus rien ne
/// nommait. Cette table est la réponse à cette question.
///
/// LA CLÉ DE STOCKAGE NE SORT PAS DU SERVICE. Les autres services manipulent
/// l'identifiant du média ; eux donner la clé les inviterait à parler au
/// stockage en direct, et l'inventaire redeviendrait faux dès la première
/// écriture qui l'aurait contourné.
/// </summary>
public sealed class MediaAsset : AggregateRoot
{
    private MediaAsset()
    {
    }

    private MediaAsset(
        Guid id,
        MediaOwnerType ownerType,
        string ownerId,
        MediaKind kind,
        string storageKey,
        string contentType,
        long sizeBytes,
        Actor uploadedBy,
        DateTimeOffset createdAt)
        : base(id)
    {
        OwnerType = ownerType;
        OwnerId = ownerId;
        Kind = kind;
        StorageKey = storageKey;
        ContentType = contentType;
        SizeBytes = sizeBytes;
        UploadedBy = uploadedBy.ToString();
        CreatedAt = createdAt;
    }

    public MediaOwnerType OwnerType { get; private set; }

    public string OwnerId { get; private set; } = string.Empty;

    public MediaKind Kind { get; private set; }

    public string StorageKey { get; private set; } = string.Empty;

    public string ContentType { get; private set; } = string.Empty;

    public long SizeBytes { get; private set; }

    /// <summary>Acteur du référentiel : « customer:&lt;id&gt; », « admin:&lt;id&gt; ».</summary>
    public string UploadedBy { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; private set; }

    public static MediaAsset Deposer(
        Guid id,
        MediaOwnerType ownerType,
        string ownerId,
        MediaKind kind,
        string storageKey,
        string contentType,
        long sizeBytes,
        Actor uploadedBy,
        DateTimeOffset now)
    {
        if (ownerType == MediaOwnerType.Unspecified)
        {
            throw new DomainException(
                "MEDIA_OWNER_REQUIRED",
                "Un média sans propriétaire ne pourrait jamais être supprimé avec lui.");
        }

        if (kind == MediaKind.Unspecified)
        {
            throw new DomainException(
                "MEDIA_KIND_REQUIRED",
                "La nature du média doit être déclarée : elle décide de sa visibilité "
                + "et de sa rétention, et ne se déduit pas du type de fichier.");
        }

        if (string.IsNullOrWhiteSpace(ownerId))
        {
            throw new DomainException("MEDIA_OWNER_REQUIRED", "Le propriétaire du média est vide.");
        }

        if (sizeBytes <= 0)
        {
            // UN FICHIER VIDE N'EST PAS UNE ERREUR DE TAILLE, C'EST UN ENVOI
            // QUI A ÉCHOUÉ. L'accepter inscrirait dans l'inventaire une ligne
            // qui promet une pièce d'identité et rend zéro octet.
            throw new DomainException("MEDIA_EMPTY", "Le fichier reçu est vide.");
        }

        return new MediaAsset(
            id,
            ownerType,
            ownerId.Trim(),
            kind,
            storageKey,
            contentType,
            sizeBytes,
            uploadedBy,
            now);
    }
}

/// <summary>
/// Qui a demandé à lire quoi, et pourquoi.
///
/// LE RÉFÉRENTIEL EXIGE UNE TRACE, ET « QUELQU'UN A LU QUELQUE CHOSE » N'EN EST
/// PAS UNE. La ligne porte le motif déclaré par le service appelant — « fiche
/// client, support », « mission en cours », « validation du dossier ». Sans lui,
/// le journal dit qu'il s'est passé quelque chose sans permettre de juger si
/// c'était légitime, ce qui est la seule question qu'on lui posera jamais.
///
/// LE PROPRIÉTAIRE QUI CONSULTE SON PROPRE FICHIER EST CONSIGNÉ AUSSI, ici,
/// contrairement au dossier livreur où il ne l'est pas. La différence : ce
/// journal-ci sert à l'exploitation du stockage, pas à surveiller un accès
/// suspect — et une lecture manquante fausserait les chiffres.
/// </summary>
public sealed class MediaAccessRecord : Entity
{
    private MediaAccessRecord()
    {
    }

    private MediaAccessRecord(Guid id, Guid mediaId, string requestedBy, string reason, DateTimeOffset at)
        : base(id)
    {
        MediaId = mediaId;
        RequestedBy = requestedBy;
        Reason = reason;
        RequestedAt = at;
    }

    public Guid MediaId { get; private set; }

    public string RequestedBy { get; private set; } = string.Empty;

    public string Reason { get; private set; } = string.Empty;

    public DateTimeOffset RequestedAt { get; private set; }

    public static MediaAccessRecord Consigner(
        Guid mediaId,
        Actor requestedBy,
        string? reason,
        DateTimeOffset now)
        => new(
            Guid.CreateVersion7(),
            mediaId,
            requestedBy.ToString(),
            string.IsNullOrWhiteSpace(reason) ? "(motif non déclaré)" : reason.Trim(),
            now);
}
