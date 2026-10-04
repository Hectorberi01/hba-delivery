using Hba.BuildingBlocks.Application.Abstractions;
using Hba.BuildingBlocks.Domain;
using Hba.Ride.Application.Common.Views;
using Hba.Ride.Application.Ports;
using Hba.Ride.Domain.Results;
using Hba.Ride.Domain.Rides;

namespace Hba.Ride.Application.Features.Rides.Commands;

/// <summary>
/// Le déroulé commun à toutes les transitions d'une course.
/// </summary>
///
/// <remarks>
/// CINQ COMMANDES FAISAIENT EXACTEMENT LA MÊME CHOSE : charger, autoriser,
/// appliquer, enregistrer, rendre la vue. Recopiée cinq fois, cette suite finit
/// par diverger — et c'est l'autorisation qu'on oublie, jamais l'enregistrement.
/// Elle est donc écrite une fois.
///
/// L'ORDRE N'EST PAS NÉGOCIABLE : ON AUTORISE AVANT D'APPLIQUER. Inversé, un
/// chauffeur non autorisé recevrait « trop tard » au lieu de « pas votre
/// course », ce qui lui apprendrait l'état d'une course qui ne le concerne pas.
///
/// L'ABSENCE EST UNE EXCEPTION, PAS UN <c>Result</c>. Ride rend des
/// <c>Result</c> pour les refus métier ; une course introuvable n'en est pas un,
/// c'est le même cas que partout ailleurs dans le dépôt, et
/// <c>NotFoundException</c> le traduit déjà en 404 / <c>NotFound</c>. Inventer
/// un code <c>RIDE_NOT_FOUND</c> obligerait la frontière de Ride à traiter à
/// part ce qu'elle traite déjà.
/// </remarks>
internal static class RideCommandFlow
{
    internal static async Task<Result<RideView>> AppliquerAsync(
        IRideRepository rides,
        ICallerContext caller,
        IUnitOfWork unitOfWork,
        Guid rideId,
        Func<ICallerContext, RideAggregate, Result?> autorisation,
        Func<RideAggregate, Actor, Result> transition,
        CancellationToken cancellationToken)
    {
        var course = await rides.GetByIdAsync(rideId, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException("Course", rideId.ToString());

        var refus = autorisation(caller, course);
        if (refus is not null)
        {
            return Result.Refus<RideView>(refus.Error.Code, refus.Error.Message);
        }

        var issue = transition(course, caller.ToActor());
        if (issue.Failed)
        {
            return Result.Refus<RideView>(issue.Error.Code, issue.Error.Message);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Ok(RideView.From(course));
    }
}
