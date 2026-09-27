namespace Hba.Driver.Application.Common.Interfaces;

/// <summary>
/// Objet depose, tel que le domaine le connait.
/// </summary>
/// <param name="Key">
/// Clé dans le stockage. C'EST ELLE QUI TRAVERSE LE CONTRAT, jamais le
/// binaire : gRPC porte des messages, pas des fichiers.
/// </param>
public sealed record StoredObject(string Key, long SizeBytes, string ContentType);

/// <summary>
/// Stockage des binaires du livreur : pièces du dossier et photo de profil.
///
/// CE PORT EXISTE POUR QUE LE CHOIX DU MOTEUR RESTE RÉVERSIBLE (ADR 0021).
/// Garage, SeaweedFS et un stockage managé parlent tous S3 ; le point 14
/// notait que le choix reste réversible **tant que rien n'importe le SDK
/// ailleurs que dans l'adaptateur**. Aucun handler, aucune entité, aucun
/// endpoint ne doit connaître `Minio` : s'ils le connaissent, la réversibilité
/// annoncée dans l'ADR est fausse, et personne ne s'en apercevra avant la
/// migration.
/// </summary>
public interface IObjectStore
{
    /// <summary>
    /// Durée de vie des URL signées.
    ///
    /// ELLE EST PUBLIÉE PAR LE PORT parce que l'écran en a besoin : il doit
    /// savoir quand recharger, sinon il affiche une image cassée. La redire
    /// dans la couche Application créerait deux vérités dont l'une finirait
    /// fausse — et ce serait l'écran qui le paierait, pas le code.
    /// </summary>
    TimeSpan ReadUrlLifetime { get; }

    /// <summary>
    /// Écrit un objet et rend sa clé.
    ///
    /// LA CLÉ EST CHOISIE PAR L'APPELANT, pas par le stockage : c'est elle qui
    /// est enregistrée en base, et une clé décidée ailleurs ne serait connue
    /// qu'après l'écriture — donc perdue si la transaction échoue.
    /// </summary>
    Task<StoredObject> PutAsync(
        string key,
        Stream content,
        long sizeBytes,
        string contentType,
        CancellationToken cancellationToken);

    /// <summary>
    /// URL de lecture, valable quelques minutes.
    ///
    /// JAMAIS D'ACCÈS PUBLIC. Le tableau de visibilité du référentiel est
    /// clair : les pièces KYC sont vues par leur propriétaire et par l'admin,
    /// « URL signée ». Un bucket ouvert en lecture, même avec des clés
    /// difficiles à deviner, ne satisfait pas cette règle.
    /// </summary>
    Task<Uri> GetReadUrlAsync(string key, CancellationToken cancellationToken);

    /// <summary>
    /// Supprime un objet. Sert au remplacement d'une pièce : la précédente
    /// n'a aucune raison de survivre à celle qui la corrige.
    /// </summary>
    Task DeleteAsync(string key, CancellationToken cancellationToken);
}

/// <summary>
/// Construction des clés.
///
/// ELLE EST ICI, PAS DANS L'ADAPTATEUR : la forme de la clé est une décision
/// métier — elle range les pièces par livreur, ce qui rend une suppression de
/// compte possible d'un préfixe. Un adaptateur qui la déciderait la ferait
/// changer à chaque changement de moteur.
/// </summary>
public static class ObjectKeys
{
    /// <summary>
    /// <c>drivers/{id}/documents/{type}/{horodatage}{extension}</c>.
    ///
    /// L'HORODATAGE EST DANS LA CLÉ, donc une pièce corrigée n'écrase pas la
    /// précédente : si un rejet porte sur la CNI, l'ancienne reste lisible le
    /// temps qu'ops compare. C'est la suppression explicite qui l'efface, pas
    /// un remplacement silencieux.
    /// </summary>
    public static string Document(Guid driverId, string documentType, DateTimeOffset now, string extension)
        => $"drivers/{driverId:N}/documents/{documentType.ToLowerInvariant()}/"
           + $"{now.UtcDateTime:yyyyMMddHHmmssfff}{Normaliser(extension)}";

    public static string ProfilePhoto(Guid driverId, DateTimeOffset now, string extension)
        => $"drivers/{driverId:N}/profile/{now.UtcDateTime:yyyyMMddHHmmssfff}{Normaliser(extension)}";

    /// <summary>Préfixe de tout ce qui appartient à un livreur.</summary>
    public static string DriverPrefix(Guid driverId) => $"drivers/{driverId:N}/";

    private static string Normaliser(string extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
        {
            return string.Empty;
        }

        var propre = extension.Trim().ToLowerInvariant();
        return propre.StartsWith('.') ? propre : "." + propre;
    }
}
