using Hba.Media.Domain.Assets;

namespace Hba.Media.Application.Assets;

public interface IMediaRepository
{
    Task<MediaAsset?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<MediaAsset>> ListAsync(
        MediaOwnerType ownerType,
        string ownerId,
        MediaKind? kind,
        CancellationToken cancellationToken);

    void Add(MediaAsset asset);

    void Remove(MediaAsset asset);

    void Consigner(MediaAccessRecord record);
}

/// <summary>
/// Forme des clés de stockage.
///
/// ELLE EST ICI, PAS DANS L'ADAPTATEUR : ranger les objets par propriétaire est
/// une décision métier — c'est elle qui rend une suppression de compte possible
/// d'un préfixe. Un adaptateur qui la déciderait la ferait changer à chaque
/// changement de moteur.
///
/// L'HORODATAGE EST DANS LA CLÉ, donc un dépôt ne peut pas en écraser un autre
/// par accident. Ce qui remplace, ce sont les règles de MediaKinds, appliquées
/// explicitement — pas une collision de noms.
/// </summary>
public static class CleDeStockage
{
    public static string Construire(
        MediaOwnerType ownerType,
        string ownerId,
        MediaKind kind,
        DateTimeOffset now,
        string extension)
        => $"{Dossier(ownerType)}/{Normaliser(ownerId)}/{kind.ToString().ToLowerInvariant()}/"
           + $"{now.UtcDateTime:yyyyMMddHHmmssfff}{Extension(extension)}";

    /// <summary>Tout ce qui appartient à quelqu'un, sous un seul préfixe.</summary>
    public static string Prefixe(MediaOwnerType ownerType, string ownerId)
        => $"{Dossier(ownerType)}/{Normaliser(ownerId)}/";

    private static string Dossier(MediaOwnerType ownerType) => ownerType switch
    {
        MediaOwnerType.Customer => "customers",
        MediaOwnerType.Driver => "drivers",
        MediaOwnerType.Merchant => "merchants",
        MediaOwnerType.Delivery => "deliveries",
        _ => "unknown",
    };

    // LES TIRETS D'UN GUID DISPARAISSENT, comme dans les clés du service Driver
    // avant la reprise : les anciennes clés « drivers/{id:N}/… » gardent ainsi
    // la même forme, et une reprise éventuelle n'a pas à réécrire des chemins.
    private static string Normaliser(string ownerId)
        => Guid.TryParse(ownerId, out var id) ? id.ToString("N") : ownerId.Trim();

    private static string Extension(string extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
        {
            return string.Empty;
        }

        var propre = extension.Trim().ToLowerInvariant();
        return propre.StartsWith('.') ? propre : "." + propre;
    }
}
