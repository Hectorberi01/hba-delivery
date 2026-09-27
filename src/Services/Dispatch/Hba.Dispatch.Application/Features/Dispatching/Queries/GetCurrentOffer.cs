using Hba.BuildingBlocks.Application.Abstractions;
using Hba.BuildingBlocks.Application.Messaging;
using Hba.BuildingBlocks.Domain;
using Hba.BuildingBlocks.Security;
using Hba.Dispatch.Application.Common.Interfaces;
using Hba.Dispatch.Application.Common.Views;

namespace Hba.Dispatch.Application.Features.Dispatching.Queries;

/// <summary>
/// Offre en cours d'un livreur, pour resynchroniser son application apres une
/// coupure. Le reseau de Cotonou rend ce cas courant, pas exceptionnel.
/// </summary>
public sealed record GetCurrentOfferQuery(string DriverId) : IQuery<OfferView?>;

public sealed class GetCurrentOfferHandler(
    IDispatchRepository dispatches,
    ICallerContext caller) : IQueryHandler<GetCurrentOfferQuery, OfferView?>
{
    public async Task<OfferView?> HandleAsync(GetCurrentOfferQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        // UN LIVREUR NE LIT QUE SA PROPRE OFFRE. Sans ce controle, le
        // parametre driver_id du contrat permettrait de balayer les courses
        // des autres — et l'apercu, lui, porte la remuneration.
        var self = caller.DriverId ?? caller.SubjectId;

        if (!string.Equals(self, query.DriverId, StringComparison.Ordinal)
            && !caller.IsInRole(HbaRoles.Ops)
            && !caller.IsInRole(HbaRoles.Admin))
        {
            throw new ForbiddenException("Cette offre n'est pas la votre.");
        }

        var dispatch = await dispatches
            .FindByPendingOfferForDriverAsync(query.DriverId, cancellationToken)
            .ConfigureAwait(false);

        var offre = dispatch?.Offers.FirstOrDefault(o => o.IsPending
            && string.Equals(o.DriverId, query.DriverId, StringComparison.Ordinal));

        return dispatch is null || offre is null ? null : OfferView.From(dispatch, offre);
    }
}
