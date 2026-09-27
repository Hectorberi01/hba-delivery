using Hba.BuildingBlocks.Application.Abstractions;
using Hba.BuildingBlocks.Application.Messaging;
using Hba.BuildingBlocks.Application.Time;
using Hba.BuildingBlocks.Domain;
using Hba.BuildingBlocks.Security;
using Hba.Driver.Application.Common.Interfaces;

namespace Hba.Driver.Application.Features.Drivers.Queries;

public sealed record GetDriverStatsQuery(TimeWindow Window) : IQuery<DriverStatsView>;

public sealed class GetDriverStatsHandler(
    IDriverStatsReader lecteur,
    ITimeCalendar calendrier,
    ICallerContext caller) : IQueryHandler<GetDriverStatsQuery, DriverStatsView>
{
    public async Task<DriverStatsView> HandleAsync(
        GetDriverStatsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        // VERIFIE ICI, PAS A LA PASSERELLE (ADR 0007). Meme reserve que
        // l'annuaire : ces compteurs disent combien de livreurs attendent une
        // validation, ce n'est pas une information publique.
        if (!caller.Roles.Overlaps(HbaRoles.BackOffice))
        {
            throw new ForbiddenException("Les effectifs livreurs sont réservés au back-office.");
        }

        return await lecteur
            .ReadAsync(calendrier.Validate(query.Window), cancellationToken)
            .ConfigureAwait(false);
    }
}
