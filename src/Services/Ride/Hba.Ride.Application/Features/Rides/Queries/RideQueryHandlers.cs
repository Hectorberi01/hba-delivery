using Hba.BuildingBlocks.Application.Abstractions;
using Hba.BuildingBlocks.Application.Messaging;
using Hba.BuildingBlocks.Domain;
using Hba.BuildingBlocks.Security;
using Hba.Ride.Application.Authorization;
using Hba.Ride.Application.Common.Views;
using Hba.Ride.Application.Ports;
using Hba.Ride.Domain.Exceptions;
using Hba.Ride.Domain.Results;

namespace Hba.Ride.Application.Features.Rides.Queries;

public sealed class GetRideByIdHandler(
    IRideRepository rides,
    ICallerContext caller) : IQueryHandler<GetRideByIdQuery, Result<RideView>>
{
    public async Task<Result<RideView>> HandleAsync(
        GetRideByIdQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var course = await rides.GetByIdAsync(query.RideId, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException("Course", query.RideId.ToString());

        var refus = RideAccess.RefusSiNePeutLire(caller, course);
        if (refus is not null)
        {
            return Result.Refus<RideView>(refus.Error.Code, refus.Error.Message);
        }

        return Result.Ok(RideView.From(course));
    }
}

public sealed class GetCustomerActiveRidesHandler(
    IRideRepository rides,
    ICallerContext caller) : IQueryHandler<GetCustomerActiveRidesQuery, Result<IReadOnlyList<RideView>>>
{
    public async Task<Result<IReadOnlyList<RideView>>> HandleAsync(
        GetCustomerActiveRidesQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (!caller.IsAuthenticated || string.IsNullOrWhiteSpace(caller.SubjectId))
        {
            return Result.Refus<IReadOnlyList<RideView>>(
                RideAccess.Forbidden, "Cette lecture demande un compte.");
        }

        var courses = await rides
            .ListActiveForCustomerAsync(caller.SubjectId, cancellationToken)
            .ConfigureAwait(false);

        return Result.Ok<IReadOnlyList<RideView>>([.. courses.Select(RideView.From)]);
    }
}

public sealed class GetCustomerRideHistoryHandler(
    IRideRepository rides,
    ICallerContext caller) : IQueryHandler<GetCustomerRideHistoryQuery, Result<RidePageView>>
{
    /// <summary>
    /// LA TAILLE DE PAGE EST BORNÉE ICI, PAS SEULEMENT À LA FRONTIÈRE. Un appel
    /// gRPC d'un autre service ne passe pas par la validation HTTP ; sans cette
    /// borne, <c>pageSize = 100000</c> chargerait tout l'historique en mémoire.
    /// </summary>
    private const int PageMaximale = 100;

    public async Task<Result<RidePageView>> HandleAsync(
        GetCustomerRideHistoryQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (!caller.IsAuthenticated || string.IsNullOrWhiteSpace(caller.SubjectId))
        {
            return Result.Refus<RidePageView>(
                RideAccess.Forbidden, "Cette lecture demande un compte.");
        }

        if (query.Offset < 0)
        {
            return Result.Refus<RidePageView>(
                RideErrorCodes.ValidationFailed, "Le décalage ne peut pas être négatif.");
        }

        var taille = Math.Clamp(query.PageSize, 1, PageMaximale);

        var (courses, total) = await rides
            .ListForCustomerAsync(caller.SubjectId, taille, query.Offset, cancellationToken)
            .ConfigureAwait(false);

        return Result.Ok(new RidePageView([.. courses.Select(RideView.From)], total));
    }
}

public sealed class GetDriverActiveRideHandler(
    IRideRepository rides,
    ICallerContext caller) : IQueryHandler<GetDriverActiveRideQuery, Result<RideView?>>
{
    public async Task<Result<RideView?>> HandleAsync(
        GetDriverActiveRideQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        // LE RÔLE NE SUFFIT PAS, L'IDENTIFIANT EST EXIGÉ. Un jeton portant le
        // rôle chauffeur sans driver_id est un jeton mal émis : le laisser
        // passer rendrait la course d'un autre, ou aucune, au hasard de la
        // requête.
        if (!caller.IsInRole(HbaRoles.Driver) || string.IsNullOrWhiteSpace(caller.DriverId))
        {
            return Result.Refus<RideView?>(
                RideAccess.Forbidden, "Cette lecture est réservée aux chauffeurs.");
        }

        var course = await rides
            .FindActiveForDriverAsync(caller.DriverId, cancellationToken)
            .ConfigureAwait(false);

        return Result.Ok<RideView?>(course is null ? null : RideView.From(course));
    }
}
