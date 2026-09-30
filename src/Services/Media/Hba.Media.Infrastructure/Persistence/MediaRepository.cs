using Hba.Media.Application.Assets;
using Hba.Media.Domain.Assets;
using Microsoft.EntityFrameworkCore;

namespace Hba.Media.Infrastructure.Persistence;

internal sealed class MediaRepository(MediaDbContext context) : IMediaRepository
{
    public Task<MediaAsset?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
        => context.Assets.FirstOrDefaultAsync(a => a.Id == id, cancellationToken);

    public async Task<IReadOnlyList<MediaAsset>> ListAsync(
        MediaOwnerType ownerType,
        string ownerId,
        MediaKind? kind,
        CancellationToken cancellationToken)
    {
        var requete = context.Assets.Where(a => a.OwnerType == ownerType && a.OwnerId == ownerId);

        if (kind is { } nature && nature != MediaKind.Unspecified)
        {
            requete = requete.Where(a => a.Kind == nature);
        }

        return await requete
            .OrderByDescending(a => a.CreatedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<MediaAsset>> ListerAvantAsync(
        MediaKind kind,
        DateTimeOffset limite,
        int lot,
        CancellationToken cancellationToken)
        // LES PLUS ANCIENS D'ABORD. Un balayage qui prendrait les plus recents
        // laisserait les plus vieux au fond indefiniment si le lot est plus
        // petit que le retard accumule.
        => await context.Assets
            .Where(a => a.Kind == kind && a.CreatedAt < limite)
            .OrderBy(a => a.CreatedAt)
            .Take(lot)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public void Add(MediaAsset asset) => context.Assets.Add(asset);

    public void Remove(MediaAsset asset) => context.Assets.Remove(asset);

    public void Consigner(MediaAccessRecord record) => context.AccessRecords.Add(record);
}
