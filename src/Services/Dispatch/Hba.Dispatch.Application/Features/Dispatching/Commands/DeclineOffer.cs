using Hba.BuildingBlocks.Application.Abstractions;
using Hba.BuildingBlocks.Application.Messaging;
using Hba.BuildingBlocks.Domain;
using Hba.BuildingBlocks.Security;
using Hba.Dispatch.Application.Common.Interfaces;

namespace Hba.Dispatch.Application.Features.Dispatching.Commands;

/// <summary>
/// Refus explicite. Il ne fait pas qu'eteindre l'offre : il libere le livreur
/// tout de suite, au lieu de le laisser reserve jusqu'a l'expiration. Trente
/// secondes de disponibilite recuperees a chaque refus.
/// </summary>
public sealed record DeclineOfferCommand(Guid OfferId, string? Reason) : ICommand<bool>;

public sealed class DeclineOfferHandler(
    IDispatchRepository dispatches,
    IUnitOfWork unitOfWork,
    ICallerContext caller,
    IClock clock) : ICommandHandler<DeclineOfferCommand, bool>
{
    public async Task<bool> HandleAsync(DeclineOfferCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var driverId = caller.DriverId ?? caller.SubjectId;

        if (string.IsNullOrWhiteSpace(driverId) || !caller.IsInRole(HbaRoles.Driver))
        {
            throw new ForbiddenException("Seul un livreur refuse une offre.");
        }

        var dispatch = await dispatches.GetByOfferIdAsync(command.OfferId, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException("Offre", command.OfferId.ToString());

        dispatch.Decline(command.OfferId, driverId, command.Reason, caller.ToActor(), clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return true;
    }
}
