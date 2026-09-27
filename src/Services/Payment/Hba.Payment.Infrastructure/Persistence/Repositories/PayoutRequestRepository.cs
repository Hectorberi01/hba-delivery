using Hba.Payment.Application.Common.Interfaces;
using Hba.Payment.Domain.Payouts;
using Microsoft.EntityFrameworkCore;

namespace Hba.Payment.Infrastructure.Persistence.Repositories;

internal sealed class PayoutRequestRepository(PaymentDbContext context) : IPayoutRequestRepository
{
    public Task<PayoutRequest?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
        => context.PayoutRequests.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    public Task<PayoutRequest?> FindPendingAsync(string driverId, CancellationToken cancellationToken)
        => context.PayoutRequests.FirstOrDefaultAsync(
            p => p.DriverId == driverId
                 && (p.Status == PayoutStatus.Requested || p.Status == PayoutStatus.Approved),
            cancellationToken);

    public async Task<IReadOnlyList<PayoutRequestView>> ListForDriverAsync(
        string driverId,
        int limit,
        CancellationToken cancellationToken)
        => await context.PayoutRequests
            .AsNoTracking()
            .Where(p => p.DriverId == driverId)
            .OrderByDescending(p => p.RequestedAt)
            .ThenByDescending(p => p.Id)
            .Take(limit)
            .Select(p => new PayoutRequestView(
                p.Id,
                p.DriverId,
                p.AmountXof,
                p.Status,
                p.RequestedAt,
                p.DecidedAt,
                p.DecidedBy,
                p.RejectionReason,
                p.PaidAt,
                p.PaymentReference))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public async Task<IReadOnlyList<PayoutRequestView>> ListForReviewAsync(
        PayoutStatus? status,
        int limit,
        CancellationToken cancellationToken)
    {
        var demandes = context.PayoutRequests.AsNoTracking();

        // LA FILE D'ATTENTE EST L'ABSENCE DE FILTRE, pas un etat parmi les
        // autres : « demandee » et « approuvee » attendent toutes deux un
        // geste de la finance, et les separer ferait oublier les approuvees
        // dont le virement n'a jamais ete consigne — celles-la, precisement,
        // sont celles qu'il faut voir.
        demandes = status is null
            ? demandes.Where(p => p.Status == PayoutStatus.Requested || p.Status == PayoutStatus.Approved)
            : demandes.Where(p => p.Status == status.Value);

        var ordonnees = status is null
            ? demandes.OrderBy(p => p.RequestedAt).ThenBy(p => p.Id)
            : demandes.OrderByDescending(p => p.RequestedAt).ThenByDescending(p => p.Id);

        return await ordonnees
            .Take(limit)
            .Select(p => new PayoutRequestView(
                p.Id,
                p.DriverId,
                p.AmountXof,
                p.Status,
                p.RequestedAt,
                p.DecidedAt,
                p.DecidedBy,
                p.RejectionReason,
                p.PaidAt,
                p.PaymentReference))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public void Add(PayoutRequest request) => context.PayoutRequests.Add(request);
}
