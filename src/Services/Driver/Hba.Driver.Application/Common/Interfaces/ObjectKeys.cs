namespace Hba.Driver.Application.Common.Interfaces;

// LE PORT ET SON ADAPTATEUR ONT QUITTE CE SERVICE le 28 septembre 2026, pour
// Hba.BuildingBlocks.Storage : Directory en avait besoin pour la photo d'un
// client, et deux copies d'un adaptateur de cent cinquante lignes derivent.
// Ce qui reste ici est ce qui appartient VRAIMENT au domaine du livreur : la
// forme de ses cles.

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
