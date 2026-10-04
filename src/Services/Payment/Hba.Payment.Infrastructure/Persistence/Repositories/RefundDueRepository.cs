using Hba.Payment.Application.Common.Interfaces;
using Hba.Payment.Domain.Payments;
using Hba.Payment.Domain.Refunds;
using Microsoft.EntityFrameworkCore;

namespace Hba.Payment.Infrastructure.Persistence.Repositories;

internal sealed class RefundDueRepository(PaymentDbContext context) : IRefundDueRepository
{
    public Task<RefundDue?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
        => context.RefundsDue.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

    public Task<RefundDue?> FindByPaymentIntentAsync(Guid paymentIntentId, CancellationToken cancellationToken)
        => context.RefundsDue.FirstOrDefaultAsync(r => r.PaymentIntentId == paymentIntentId, cancellationToken);

    public async Task<(IReadOnlyList<RefundDue> Lignes, int Total)> ListAsync(
        RefundDueStatus? status,
        ServiceType? serviceType,
        int pageSize,
        int offset,
        CancellationToken cancellationToken)
    {
        var requete = context.RefundsDue.AsNoTracking();

        if (status is not null)
        {
            requete = requete.Where(r => r.Status == status);
        }

        if (serviceType is not null)
        {
            requete = requete.Where(r => r.ServiceType == serviceType);
        }

        // LE TOTAL EST COMPTE SUR LA MEME REQUETE, avant la page : la console affiche
        // « 3 sur 47 », et un total deduit de la page rendue dirait toujours la
        // taille de la page.
        var total = await requete.CountAsync(cancellationToken).ConfigureAwait(false);

        // LE PLUS ANCIEN D'ABORD, A L'INVERSE DES AUTRES LISTES DU DEPOT. Un
        // remboursement qui attend depuis trois jours est plus urgent que celui d'il
        // y a une heure : trier par date decroissante enterrerait les oublis sous les
        // nouveaux, et un oubli ici est de l'argent retenu.
        var lignes = await requete
            .OrderBy(r => r.CreatedAt)
            .ThenBy(r => r.Id)
            .Skip(offset)
            .Take(pageSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return (lignes, total);
    }

    public void Add(RefundDue refund) => context.RefundsDue.Add(refund);
}
