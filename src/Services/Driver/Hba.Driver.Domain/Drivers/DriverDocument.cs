using Hba.BuildingBlocks.Domain;
using Hba.Driver.Domain.Exceptions;

namespace Hba.Driver.Domain.Drivers;

/// <summary>
/// Nature d'une piece du dossier.
///
/// LA LISTE VIENT DU REFERENTIEL, mot pour mot : « documents KYC (CNI,
/// permis, carte grise, photo — dans MinIO) ». Aucune piece n'a ete ajoutee.
/// </summary>
public enum DocumentType
{
    Unspecified = 0,

    /// <summary>Carte nationale d'identite.</summary>
    NationalId = 1,

    DrivingLicence = 2,

    /// <summary>Carte grise du vehicule.</summary>
    VehicleRegistration = 3,

    /// <summary>
    /// Photo d'identite du livreur, PIECE DU DOSSIER. A ne pas confondre avec
    /// la photo de profil : celle-ci prouve une identite et passe par
    /// l'examen, l'autre sert seulement au client a reconnaitre qui arrive
    /// (ADR 0021).
    /// </summary>
    IdentityPhoto = 4,

    /// <summary>Photo du vehicule, pour qu'ops verifie plaque et etat.</summary>
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
    private DriverDocument()
    {
    }

    public DocumentType Type { get; private set; }

    /// <summary>Cle dans le stockage objet. Jamais une URL : une URL expire.</summary>
    public string ObjectKey { get; private set; } = string.Empty;

    public string ContentType { get; private set; } = string.Empty;

    public long SizeBytes { get; private set; }

    public DateTimeOffset UploadedAt { get; private set; }

    public static DriverDocument Create(
        DocumentType type,
        string objectKey,
        string contentType,
        long sizeBytes,
        DateTimeOffset uploadedAt)
    {
        if (type == DocumentType.Unspecified)
        {
            throw new DomainException(
                DriverErrorCodes.UnknownDocumentType,
                "La nature de la piece doit etre precisee.");
        }

        if (string.IsNullOrWhiteSpace(objectKey))
        {
            throw new DomainException(
                DriverErrorCodes.MissingObjectKey,
                "Une piece sans cle de stockage serait introuvable.");
        }

        if (sizeBytes <= 0)
        {
            throw new DomainException(
                DriverErrorCodes.EmptyDocument,
                "Une piece vide ne prouve rien.");
        }

        // L'IDENTIFIANT RESTE VIDE, ET C'EST EF QUI LE POSE. Ce n'est pas un
        // oubli, c'est la seule facon d'etre insere.
        //
        // Quand EF decouvre un enfant dans une collection possedee dont le
        // proprietaire est DEJA EN BASE, il tranche « nouveau ou existant ? »
        // en regardant si la cle est renseignee. Une cle posee par le domaine
        // lui fait conclure que la ligne existe : il passe l'entree en
        // Modified — ce qui marque TOUTES les proprietes modifiees, jusqu'a
        // la cle etrangere — et emet un UPDATE sur une ligne jamais ecrite.
        // Zero ligne affectee, DbUpdateConcurrencyException, et un message de
        // concurrence pour un premier depot ou personne n'etait en course.
        //
        // Laisser la cle a sa valeur par defaut renverse la reponse : EF voit
        // une entite neuve, l'ajoute, et appelle le generateur declare dans
        // DriverConfiguration — qui rend un GUID v7, comme avant. Le domaine
        // perd seulement le droit de nommer la piece avant qu'elle existe.
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
    ///
    /// POURQUOI PAS UN RETRAIT SUIVI D'UN AJOUT. Retirer l'entite de la
    /// collection et en ajouter une autre fait produire a EF Core un DELETE
    /// puis un INSERT sur la meme cle d'unicite (driver_id, type). Si deux
    /// depots de la meme piece arrivent ensemble — un double appui, un
    /// renvoi du client apres un reseau lent — le premier valide, et le
    /// DELETE du second ne trouve plus rien. EF compte zero ligne la ou il
    /// en attendait une et leve DbUpdateConcurrencyException : le livreur
    /// recoit une erreur 500 pour un depot qui, du point de vue metier,
    /// n'avait rien d'anormal. Un UPDATE sur la meme ligne reste vrai que le
    /// depot soit le premier ou le troisieme.
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
