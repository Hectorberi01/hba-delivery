using Hba.BuildingBlocks.Application.Abstractions;
using Hba.BuildingBlocks.Application.Messaging;
using Hba.BuildingBlocks.Application.Time;
using Hba.BuildingBlocks.Domain;
using Hba.BuildingBlocks.Security;
using Hba.Dispatch.Application.Common.Interfaces;

namespace Hba.Dispatch.Application.Features.Dispatching.Queries;

public sealed record GetDispatchStatsQuery(TimeWindow Window) : IQuery<DispatchStatsView>;

public sealed class GetDispatchStatsHandler(
    IDispatchStatsReader lecteur,
    ITimeCalendar calendrier,
    ICallerContext caller) : IQueryHandler<GetDispatchStatsQuery, DispatchStatsView>
{
    public async Task<DispatchStatsView> HandleAsync(
        GetDispatchStatsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        // VERIFIE ICI, PAS A LA PASSERELLE (ADR 0007).
        if (!caller.Roles.Overlaps(HbaRoles.BackOffice))
        {
            throw new ForbiddenException("Les indicateurs de dispatch sont réservés au back-office.");
        }

        // PAS DE SERIE ICI, DONC PAS DE PERIODES VIDES A RECOLLER. Ce bloc
        // est une liste de nombres, pas une courbe : le graphique du volume
        // est celui de Delivery, et le doubler ici donnerait deux chiffres
        // pour la meme chose.
        return await lecteur
            .ReadAsync(calendrier.Validate(query.Window), cancellationToken)
            .ConfigureAwait(false);
    }
}
