using Hba.BuildingBlocks.Application.Abstractions;
using Hba.BuildingBlocks.Application.Messaging;
using Hba.Ride.Application.Authorization;
using Hba.Ride.Application.Common.Views;
using Hba.Ride.Application.Ports;
using Hba.Ride.Domain.Results;

namespace Hba.Ride.Application.Features.Rides.Commands;

// Tous ces gestionnaires passent par RideCommandFlow : charger, autoriser,
// appliquer, enregistrer. Ce qui change d'un à l'autre tient en deux lignes —
// qui a le droit, et quelle transition — et c'est précisément ce qu'on veut
// pouvoir lire d'un coup d'œil.

public sealed class DriverIsOnTheWayHandler(
    IRideRepository rides,
    ICallerContext caller,
    IUnitOfWork unitOfWork,
    IClock clock) : ICommandHandler<DriverIsOnTheWayCommand, Result<RideView>>
{
    public Task<Result<RideView>> HandleAsync(
        DriverIsOnTheWayCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return RideCommandFlow.AppliquerAsync(
            rides,
            caller,
            unitOfWork,
            command.RideId,
            RideAccess.RefusSiPasLeChauffeur,
            (course, acteur) => course.DriverIsOnTheWay(acteur, clock.UtcNow),
            cancellationToken);
    }
}

public sealed class DriverHasArrivedHandler(
    IRideRepository rides,
    ICallerContext caller,
    IUnitOfWork unitOfWork,
    IClock clock) : ICommandHandler<DriverHasArrivedCommand, Result<RideView>>
{
    public Task<Result<RideView>> HandleAsync(
        DriverHasArrivedCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return RideCommandFlow.AppliquerAsync(
            rides,
            caller,
            unitOfWork,
            command.RideId,
            RideAccess.RefusSiPasLeChauffeur,
            (course, acteur) => course.DriverHasArrived(acteur, clock.UtcNow),
            cancellationToken);
    }
}

public sealed class StartRideHandler(
    IRideRepository rides,
    ICallerContext caller,
    IUnitOfWork unitOfWork,
    IClock clock) : ICommandHandler<StartRideCommand, Result<RideView>>
{
    public Task<Result<RideView>> HandleAsync(
        StartRideCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return RideCommandFlow.AppliquerAsync(
            rides,
            caller,
            unitOfWork,
            command.RideId,
            RideAccess.RefusSiPasLeChauffeur,
            (course, acteur) => course.Start(acteur, clock.UtcNow),
            cancellationToken);
    }
}

public sealed class CompleteRideHandler(
    IRideRepository rides,
    ICallerContext caller,
    IUnitOfWork unitOfWork,
    IClock clock) : ICommandHandler<CompleteRideCommand, Result<RideView>>
{
    public Task<Result<RideView>> HandleAsync(
        CompleteRideCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return RideCommandFlow.AppliquerAsync(
            rides,
            caller,
            unitOfWork,
            command.RideId,
            RideAccess.RefusSiPasLeChauffeur,
            (course, acteur) => course.Complete(acteur, clock.UtcNow),
            cancellationToken);
    }
}

public sealed class CancelRideHandler(
    IRideRepository rides,
    ICallerContext caller,
    IUnitOfWork unitOfWork,
    IClock clock) : ICommandHandler<CancelRideCommand, Result<RideView>>
{
    public Task<Result<RideView>> HandleAsync(
        CancelRideCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return RideCommandFlow.AppliquerAsync(
            rides,
            caller,
            unitOfWork,
            command.RideId,
            RideAccess.RefusSiNePeutAnnuler,
            (course, acteur) => course.Cancel(command.Reason, acteur, clock.UtcNow),
            cancellationToken);
    }
}
