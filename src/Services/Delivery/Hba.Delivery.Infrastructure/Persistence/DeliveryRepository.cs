using Hba.Delivery.Application.Ports;
using Hba.Delivery.Domain.Deliveries;
using Microsoft.EntityFrameworkCore;

namespace Hba.Delivery.Infrastructure.Persistence;

internal sealed class DeliveryRepository(DeliveryDbContext context) : IDeliveryRepository
{
    public Task<DeliveryAggregate?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
        => context.Deliveries.FirstOrDefaultAsync(d => d.Id == id, cancellationToken);

    public Task<DeliveryAggregate?> GetByExternalOrderIdAsync(
        string partnerId,
        string externalOrderId,
        CancellationToken cancellationToken)
        => context.Deliveries.FirstOrDefaultAsync(
            d => d.PartnerId == partnerId && d.ExternalOrderId == externalOrderId,
            cancellationToken);

    public async Task<IReadOnlyList<DeliveryAggregate>> ListAsync(
        DeliveryQueryFilter filter,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var query = context.Deliveries.AsNoTracking().AsQueryable();

        if (filter.CustomerId is not null)
        {
            query = query.Where(d => d.CustomerId == filter.CustomerId);
        }

        if (filter.MerchantId is not null)
        {
            query = query.Where(d => d.MerchantId == filter.MerchantId);
        }

        if (filter.PartnerId is not null)
        {
            query = query.Where(d => d.PartnerId == filter.PartnerId);
        }

        if (filter.DriverId is not null)
        {
            query = query.Where(d => d.Driver != null && d.Driver.DriverId == filter.DriverId);
        }

        if (filter.Statuses is { Count: > 0 })
        {
            var statuses = filter.Statuses.ToList();
            query = query.Where(d => statuses.Contains(d.Status));
        }

        // APRES le filtre demandé, et jamais avant : ce que la couche
        // Application retire ici ne se rattrape par aucun paramètre.
        if (filter.ExcludedStatuses is { Count: > 0 })
        {
            var exclus = filter.ExcludedStatuses.ToList();
            query = query.Where(d => !exclus.Contains(d.Status));
        }

        if (filter.CreatedAfter is not null)
        {
            query = query.Where(d => d.CreatedAt >= filter.CreatedAfter);
        }

        if (filter.CreatedBefore is not null)
        {
            query = query.Where(d => d.CreatedAt <= filter.CreatedBefore);
        }

        return await query
            .OrderByDescending(d => d.CreatedAt)
            .Skip(filter.Offset)
            .Take(filter.PageSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<DeliveryAggregate>> ListUnpaidBeforeAsync(
        DateTimeOffset limite,
        int batchSize,
        CancellationToken cancellationToken)
    {
        // « PaymentIntentId != null » EST LA GARDE QUI PROTEGE LE B2B : une
        // commande de partenaire naît dans le même statut et n'a pas
        // d'intention. Voir le commentaire du port.
        //
        // LES PLUS ANCIENNES D'ABORD : après une panne, c'est le retard le plus
        // vieux qu'il faut solder en premier, et c'est aussi ce qui rend le
        // balayage progressif au lieu de tourner en rond sur les mêmes lignes.
        return await context.Deliveries
            .Where(d => d.Status == DeliveryStatus.PendingPayment
                && d.PaymentIntentId != null
                && d.CreatedAt <= limite)
            .OrderBy(d => d.CreatedAt)
            .Take(Math.Clamp(batchSize, 1, 500))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<(int Count, long BilledTotal)> SumBilledForCustomerAsync(
        string customerId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(customerId);

        // LIVREES SEULEMENT. Une course annulee ou echouee n'est pas
        // facturee ; l'inclure gonflerait le total d'un client dont la
        // moitie des commandes n'ont jamais abouti.
        var query = context.Deliveries
            .AsNoTracking()
            .Where(d => d.CustomerId == customerId && d.Status == DeliveryStatus.Delivered);

        // DEUX AGREGATS, UNE SEULE REQUETE. GroupBy sur une constante est la
        // forme qu'EF traduit en un SELECT COUNT(*), SUM(...) ; deux appels
        // separes feraient deux allers-retours pour deux nombres lus
        // ensemble, et rien ne garantirait qu'ils voient la meme base.
        var agregat = await query
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Count = g.Count(),
                Total = g.Sum(d => d.Pricing.Total.Amount),
            })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return agregat is null ? (0, 0L) : (agregat.Count, agregat.Total);
    }

    public void Add(DeliveryAggregate delivery) => context.Deliveries.Add(delivery);
}
