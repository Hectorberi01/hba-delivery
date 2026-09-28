namespace Hba.BuildingBlocks.Storage;

/// <summary>
/// Objet deposé, tel que le domaine le connaît.
/// </summary>
/// <param name="Key">
/// Clé dans le stockage. C'EST ELLE QUI TRAVERSE LE CONTRAT, jamais le
/// binaire : gRPC porte des messages, pas des fichiers.
/// </param>
public sealed record StoredObject(string Key, long SizeBytes, string ContentType);

/// <summary>
/// Stockage des binaires : pièces du dossier livreur, photos de profil.
///
/// CE PORT EXISTE POUR QUE LE CHOIX DU MOTEUR RESTE RÉVERSIBLE (ADR 0021).
/// Garage, SeaweedFS et un stockage managé parlent tous S3 ; le point 14
/// notait que le choix reste réversible **tant que rien n'importe le SDK
/// ailleurs que dans l'adaptateur**. Aucun handler, aucune entité, aucun
/// endpoint ne doit connaître `Minio` : s'ils le connaissent, la réversibilité
/// annoncée dans l'ADR est fausse, et personne ne s'en apercevra avant la
/// migration.
///
/// IL A QUITTÉ LE SERVICE DRIVER LE 28 SEPTEMBRE 2026, quand Directory a eu
/// besoin d'écrire la photo d'un client. Le dépôt duplique volontiers un
/// objet-valeur de vingt lignes — PhoneNumber vit dans chaque service — mais un
/// adaptateur de cent cinquante lignes en double dérive : un correctif appliqué
/// d'un côté manque de l'autre, et personne ne s'en aperçoit avant la panne.
/// La règle de l'ADR tient toujours : le SDK ne sort pas d'ici.
///
/// CE QUI RESTE AU SERVICE, EN REVANCHE, C'EST LA FORME DES CLÉS. Ranger les
/// objets par livreur ou par client est une décision métier — c'est elle qui
/// rend une suppression de compte possible d'un préfixe — et chaque service
/// garde la sienne.
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
    /// Supprime un objet. Sert au remplacement d'une pièce ou d'une photo : la
    /// précédente n'a aucune raison de survivre à celle qui la remplace.
    /// </summary>
    Task DeleteAsync(string key, CancellationToken cancellationToken);
}
