using Hba.BuildingBlocks.Application.Abstractions;
using Hba.BuildingBlocks.Application.Messaging;
using Hba.Payment.Application.Common.Interfaces;
using Hba.Payment.Domain.Payments;
using Hba.Payment.Domain.Refunds;

namespace Hba.Payment.Application.Features.Refunds.Queries;

/// <summary>
/// La file des remboursements. Sans filtre d'etat, elle rend CE QUI RESTE A FAIRE :
/// c'est la question que se pose la finance en ouvrant l'ecran, et une file qui
/// s'ouvrirait sur l'historique complet cacherait le travail du jour.
/// </summary>
public sealed record ListRefundsDueQuery(
    RefundDueStatus? Status = RefundDueStatus.Pending,
    ServiceType? ServiceType = null,
    int PageSize = 50,
    int Offset = 0) : IQuery<RefundDuePageView>;

public sealed class ListRefundsDueHandler(
    IRefundDueRepository refunds,
    ICallerContext caller) : IQueryHandler<ListRefundsDueQuery, RefundDuePageView>
{
    private const int PageMaximale = 200;

    public async Task<RefundDuePageView> HandleAsync(
        ListRefundsDueQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        RefundAccess.EnsureCanRead(caller);

        var taille = Math.Clamp(query.PageSize, 1, PageMaximale);
        var decalage = Math.Max(query.Offset, 0);

        var (lignes, total) = await refunds
            .ListAsync(query.Status, query.ServiceType, taille, decalage, cancellationToken)
            .ConfigureAwait(false);

        return new RefundDuePageView([.. lignes.Select(RefundDueView.From)], total);
    }
}
