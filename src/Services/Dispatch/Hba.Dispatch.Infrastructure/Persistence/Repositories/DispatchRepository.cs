using Hba.Dispatch.Application.Common.Interfaces;
using Hba.Dispatch.Domain.Dispatching;
using Microsoft.EntityFrameworkCore;

namespace Hba.Dispatch.Infrastructure.Persistence.Repositories;

internal sealed class DispatchRepository(DispatchDbContext context) : IDispatchRepository
{
    public Task<DispatchAggregate?> GetByDeliveryIdAsync(Guid deliveryId, CancellationToken cancellationToken)
        => context.Dispatches.FirstOrDefaultAsync(d => d.DeliveryId == deliveryId, cancellationToken);

    public Task<DispatchAggregate?> GetByOfferIdAsync(Guid offerId, CancellationToken cancellationToken)
        => context.Dispatches.FirstOrDefaultAsync(d => d.Offers.Any(o => o.Id == offerId), cancellationToken);

    public Task<DispatchAggregate?> FindByPendingOfferForDriverAsync(
        string driverId,
        CancellationToken cancellationToken)
        => context.Dispatches.FirstOrDefaultAsync(
            d => d.Offers.Any(o => o.DriverId == driverId && o.Status == OfferStatus.Pending),
            cancellationToken);

    /// <summary>
    /// Recherches que le planificateur doit regarder.
    ///
    /// LE FILTRE EST LARGE A DESSEIN : toutes les recherches ouvertes. Vouloir
    /// ne rendre que celles qui « ont vraiment quelque chose a faire »
    /// demanderait de reproduire en SQL la regle de <c>WaveIsSettled</c>, donc
    /// de la tenir juste a deux endroits. Le volume ne le justifie pas : une
    /// recherche vit quatre-vingt-dix secondes, il n'y en a jamais beaucoup
    /// d'ouvertes en meme temps.
    /// </summary>
    public async Task<IReadOnlyList<DispatchAggregate>> FindDueAsync(
        DateTimeOffset now,
        int batchSize,
        CancellationToken cancellationToken)
        => await context.Dispatches
            .Where(d => d.Status == DispatchStatus.Searching)
            .OrderBy(d => d.CreatedAt)
            .Take(batchSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public void Add(DispatchAggregate dispatch) => context.Dispatches.Add(dispatch);
}
