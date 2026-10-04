using Hba.BuildingBlocks.Application.Abstractions;
using Hba.BuildingBlocks.Application.Messaging;
using Hba.BuildingBlocks.Domain;
using Hba.Ride.Application.Ports;
using Hba.Ride.Domain.Results;
using Hba.Ride.Domain.Rides;

namespace Hba.Ride.Application.Features.Rides.Commands;

public sealed class StartRideSearchHandler(
    IRideRepository rides,
    IUnitOfWork unitOfWork) : ICommandHandler<StartRideSearchCommand, Result>
{
    public async Task<Result> HandleAsync(
        StartRideSearchCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var course = await rides.GetByIdAsync(command.RideId, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException("Course", command.RideId.ToString());

        // LE REJEU NE DOIT PAS REFUSER. Kafka livre au moins une fois : la même
        // vague peut arriver deux fois, et la recherche est alors déjà ouverte.
        if (course.Status == RideStatus.SearchingDriver)
        {
            return Result.Ok();
        }

        var issue = course.StartSearching(Actor.DispatchEngine, command.OccurredAt);
        if (issue.Failed)
        {
            return issue;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Ok();
    }
}

public sealed class AssignRideDriverHandler(
    IRideRepository rides,
    IRideDriverDirectory drivers,
    IUnitOfWork unitOfWork) : ICommandHandler<AssignRideDriverCommand, Result>
{
    public async Task<Result> HandleAsync(
        AssignRideDriverCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var course = await rides.GetByIdAsync(command.RideId, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException("Course", command.RideId.ToString());

        // LE MÊME CHAUFFEUR DÉJÀ AFFECTÉ EST UN REJEU, PAS UN CONFLIT. Sans ce
        // test, la seconde livraison du même message recevrait
        // DRIVER_ALREADY_ASSIGNED, le consommateur réessaierait trois fois puis
        // abandonnerait le message — sur une course pourtant correcte.
        if (string.Equals(course.DriverId, command.DriverId, StringComparison.Ordinal))
        {
            return Result.Ok();
        }

        // La recherche peut ne pas être encore ouverte de ce côté : Dispatch
        // accepte parfois une offre avant que la première vague ne soit traitée
        // ici. On ouvre plutôt que d'échouer, comme le fait Delivery.
        if (course.Status == RideStatus.Requested)
        {
            var ouverture = course.StartSearching(Actor.DispatchEngine, command.AssignedAt);
            if (ouverture.Failed)
            {
                return ouverture;
            }
        }

        // LE PROFIL EST LU AVANT D'AFFECTER, ET SON ABSENCE NE BLOQUE PAS.
        //
        // Il sert a deux choses : afficher le chauffeur au passager sans rappeler
        // Driver a chaque lecture, et ecrire le SMS « {driver} vient vous chercher ».
        // Driver injoignable a cet instant rend null : l'affectation passe, le
        // passager lit « un chauffeur arrive », et c'est tres superieur a une
        // acceptation refusee alors qu'elle est deja actee chez Dispatch et chez
        // Driver — le chauffeur resterait reserve sur une course qui l'ignore.
        var profil = await drivers
            .GetPublicProfileAsync(command.DriverId, cancellationToken)
            .ConfigureAwait(false);

        var issue = course.AssignDriver(command.DriverId, profil, Actor.DispatchEngine, command.AssignedAt);
        if (issue.Failed)
        {
            return issue;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Ok();
    }
}

public sealed class MarkNoRideDriverFoundHandler(
    IRideRepository rides,
    IUnitOfWork unitOfWork) : ICommandHandler<MarkNoRideDriverFoundCommand, Result>
{
    public async Task<Result> HandleAsync(
        MarkNoRideDriverFoundCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var course = await rides.GetByIdAsync(command.RideId, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException("Course", command.RideId.ToString());

        if (course.Status == RideStatus.NoDriverAvailable)
        {
            return Result.Ok();
        }

        // LA COURSE A PU ÊTRE ANNULÉE PENDANT LA RECHERCHE, et c'est banal : le
        // passager se lasse avant la dernière vague. Constater l'échec d'une
        // recherche déjà close n'apporte rien et ne doit pas faire échouer le
        // message.
        if (course.EstClose)
        {
            return Result.Ok();
        }

        var issue = course.NoDriverFound(Actor.DispatchEngine, command.OccurredAt);
        if (issue.Failed)
        {
            return issue;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Ok();
    }
}

public sealed class FailRidePaymentHandler(
    IRideRepository rides,
    IUnitOfWork unitOfWork) : ICommandHandler<FailRidePaymentCommand, Result>
{
    public async Task<Result> HandleAsync(
        FailRidePaymentCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var course = await rides.GetByIdAsync(command.RideId, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException("Course", command.RideId.ToString());

        // UNE COURSE DEJA CLOSE N'A PLUS DE PAIEMENT A FAIRE ECHOUER. Le passager a
        // pu annuler pendant que la page de paiement expirait : la course est
        // annulee, et c'est l'etat juste — l'echec du paiement n'apporte rien et ne
        // doit pas faire echouer le message.
        if (course.EstClose)
        {
            return Result.Ok();
        }

        var issue = course.MarkPaymentFailed(command.Reason, Actor.FedaPay, command.OccurredAt);
        if (issue.Failed)
        {
            return issue;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Ok();
    }
}
