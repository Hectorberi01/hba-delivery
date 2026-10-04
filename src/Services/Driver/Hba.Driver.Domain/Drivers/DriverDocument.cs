using Hba.BuildingBlocks.Domain;
using Hba.Driver.Domain.Exceptions;

namespace Hba.Driver.Domain.Drivers;

/// <summary>
/// Nature d'une piece du dossier.
/// </summary>
public enum DocumentType
{
    Unspecified = 0,
    NationalId = 1,
    DrivingLicence = 2,
    VehicleRegistration = 3,
    IdentityPhoto = 4,
    VehiclePhoto = 5,
}

/// <summary>
/// Une piece deposee.
///
/// ELLE NE PORTE PAS LE BINAIRE, seulement sa cle dans le stockage objet. Le
/// domaine ne sait pas ce qu'est un octet ; il sait qu'une piece existe,
/// quand elle a ete deposee, et ou la retrouver.
/// </summary>
public sealed class DriverDocument : Entity
{
    public DocumentType Type { get; private set; }
    /// <summary>Cle dans le stockage objet. Jamais une URL : une URL expire.</summary>
    public string ObjectKey { get; private set; } = string.Empty;
    public string ContentType { get; private set; } = string.Empty;
    public long SizeBytes { get; private set; }
    public DateTimeOffset UploadedAt { get; private set; }

    private DriverDocument() { }
    public static DriverDocument Create(DocumentType type, string objectKey, string contentType, long sizeBytes, DateTimeOffset uploadedAt)
    {
        if (type == DocumentType.Unspecified)
        {
            throw new DomainException(DriverErrorCodes.UnknownDocumentType, "La nature de la piece doit etre precisee.");
        }

        if (string.IsNullOrWhiteSpace(objectKey))
        {
            throw new DomainException(DriverErrorCodes.MissingObjectKey, "Une piece sans cle de stockage serait introuvable.");
        }

        if (sizeBytes <= 0)
        {
            throw new DomainException(DriverErrorCodes.EmptyDocument, "Une piece vide ne prouve rien.");
        }

        return new DriverDocument
        {
            Type = type,
            ObjectKey = objectKey.Trim(),
            ContentType = contentType,
            SizeBytes = sizeBytes,
            UploadedAt = uploadedAt,
        };
    }

    /// <summary>
    /// Nouvelle version de la meme piece : on modifie la ligne, on ne la
    /// remplace pas. Rend la cle de l'objet devenu inutile.
    /// </summary>
    internal string ReplaceWith(DriverDocument autre)
    {
        ArgumentNullException.ThrowIfNull(autre);

        var ancienne = ObjectKey;

        ObjectKey = autre.ObjectKey;
        ContentType = autre.ContentType;
        SizeBytes = autre.SizeBytes;
        UploadedAt = autre.UploadedAt;

        return ancienne;
    }
}
