using Hba.BuildingBlocks.Application.Messaging;
using Hba.BuildingBlocks.Application.Abstractions;
using Hba.Payment.Application.Common.Interfaces;
using Hba.Payment.Domain.Payouts;

namespace Hba.Payment.Application.Features.Payouts.Queries;

/// <summary>
/// La file des demandes de versement, pour la finance.
///
/// <paramref name="Status"/> ABSENT VEUT DIRE « CE QUI ATTEND », pas « tout ».
/// C'est l'ecran d'ouverture de la finance : les demandes vivantes, la plus
/// ancienne d'abord. Rendre tout par defaut noierait les cinq demandes du jour
/// dans l'historique de six mois, et la file cesserait d'etre une file.
/// </summary>
public sealed record ListPayoutRequestsQuery(PayoutStatus? Status, int Limit)
    : IQuery<IReadOnlyList<PayoutRequestView>>;

public sealed class ListPayoutRequestsHandler(
    IPayoutRequestRepository payouts,
    ICallerContext caller) : IQueryHandler<ListPayoutRequestsQuery, IReadOnlyList<PayoutRequestView>>
{
    private const int DefaultLimit = 50;

    private const int MaxLimit = 200;

    public async Task<IReadOnlyList<PayoutRequestView>> HandleAsync(
        ListPayoutRequestsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        PayoutReview.EnsureReviewer(caller);

        var plafond = query.Limit <= 0 ? DefaultLimit : Math.Min(query.Limit, MaxLimit);

        return await payouts
            .ListForReviewAsync(query.Status, plafond, cancellationToken)
            .ConfigureAwait(false);
    }
}
