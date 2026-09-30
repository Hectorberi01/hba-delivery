using Hba.BuildingBlocks.Domain;
using Hba.Media.Application.Assets;
using Hba.Media.Domain.Assets;

namespace Hba.Media.Application.Tests;

/// <summary>
/// Un inventaire tenu en mémoire, pour éprouver ce que rend un gestionnaire.
/// </summary>
///
/// <remarks>
/// CE QUI SE TESTE AVEC LUI N'EST PAS LA BASE, C'EST LE TRI QUI SUIT LA LECTURE.
/// <c>ListMediaHandler</c> interroge le dépôt SANS filtrer par nature, puis
/// retire ce qu'il refuserait de rendre ; c'est donc ce deuxième temps qu'il
/// faut pouvoir observer, et un dépôt qui rend tout ce qu'on lui a confié suffit
/// pour cela.
/// </remarks>
internal sealed class InventaireFactice : IMediaRepository
{
    private readonly List<MediaAsset> _medias = [];

    public InventaireFactice Avec(MediaOwnerType ownerType, string ownerId, MediaKind kind)
    {
        _medias.Add(MediaAsset.Deposer(
            Guid.CreateVersion7(),
            ownerType,
            ownerId,
            kind,
            CleDeStockage.Construire(ownerType, ownerId, kind, DateTimeOffset.UtcNow, ".jpg"),
            "image/jpeg",
            1024,
            Actor.Admin("u-test"),
            DateTimeOffset.UtcNow));

        return this;
    }

    public Task<IReadOnlyList<MediaAsset>> ListAsync(
        MediaOwnerType ownerType,
        string ownerId,
        MediaKind? kind,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<MediaAsset> trouves = _medias
            .Where(m => m.OwnerType == ownerType
                        && string.Equals(m.OwnerId, ownerId, StringComparison.OrdinalIgnoreCase)
                        && (kind is null || m.Kind == kind))
            .ToList();

        return Task.FromResult(trouves);
    }

    public Task<MediaAsset?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
        => Task.FromResult<MediaAsset?>(_medias.Find(m => m.Id == id));

    public Task<IReadOnlyList<MediaAsset>> ListerAvantAsync(
        MediaKind kind,
        DateTimeOffset limite,
        int lot,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<MediaAsset> trouves = _medias
            .Where(m => m.Kind == kind && m.CreatedAt < limite)
            .OrderBy(m => m.CreatedAt)
            .Take(lot)
            .ToList();

        return Task.FromResult(trouves);
    }

    public void Add(MediaAsset asset) => _medias.Add(asset);

    public void Remove(MediaAsset asset) => _medias.Remove(asset);

    public void Consigner(MediaAccessRecord record)
    {
        // LA CONSIGNATION NE CHANGE RIEN À CE QUI EST RENDU ; elle n'a donc pas
        // sa place dans les assertions de ce fichier.
    }
}
