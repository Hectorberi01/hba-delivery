using Hba.BuildingBlocks.Application.Abstractions;
using Hba.BuildingBlocks.Application.Messaging;
using Hba.BuildingBlocks.Domain;
using Hba.BuildingBlocks.Security;
using Hba.Dispatch.Application.Common.Interfaces;
using Hba.Dispatch.Application.Common.Views;
using Hba.Dispatch.Domain.Exceptions;
using Microsoft.Extensions.Options;

namespace Hba.Dispatch.Application.Features.Dispatching.Commands;

/// <summary>
/// Un livreur accepte une offre.
///
/// LE CHEMIN LE PLUS DISPUTE DU SYSTEME. Cinq livreurs ont recu la meme offre
/// et appuient dans la meme seconde ; il doit en rester un, et les quatre
/// autres doivent comprendre pourquoi ils ont perdu.
/// </summary>
public sealed record AcceptOfferCommand(Guid OfferId, string? IdempotencyKey) : ICommand<AcceptOfferResult>;

public sealed class AcceptOfferHandler(
    IDispatchRepository dispatches,
    IAcceptanceLock locks,
    IUnitOfWork unitOfWork,
    IOptions<DispatchOptions> options,
    ICallerContext caller,
    IClock clock) : ICommandHandler<AcceptOfferCommand, AcceptOfferResult>
{
    private readonly DispatchOptions _options = options.Value;

    public async Task<AcceptOfferResult> HandleAsync(
        AcceptOfferCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var driverId = caller.DriverId ?? caller.SubjectId;

        if (string.IsNullOrWhiteSpace(driverId) || !caller.IsInRole(HbaRoles.Driver))
        {
            throw new ForbiddenException("Seul un livreur accepte une offre.");
        }

        var dispatch = await dispatches.GetByOfferIdAsync(command.OfferId, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException("Offre", command.OfferId.ToString());

        var jeton = await locks
            .TryAcquireAsync(dispatch.DeliveryId, TimeSpan.FromSeconds(_options.AcceptanceLockSeconds), cancellationToken)
            .ConfigureAwait(false);

        if (jeton is null)
        {
            // QUELQU'UN D'AUTRE EST EN TRAIN D'ACCEPTER. Ce n'est pas encore
            // certain qu'il gagne, mais c'est la reponse utile au livreur : il
            // n'a rien a reessayer.
            return new AcceptOfferResult(false, DispatchErrorCodes.AlreadyTaken, null);
        }

        try
        {
            var offre = dispatch.Accept(command.OfferId, driverId, caller.ToActor(), clock.UtcNow);

            // Le jeton de concurrence optimiste de l'agregat est la troisieme
            // barriere : si le verrou avait expire et qu'une autre acceptation
            // etait passee, ce SaveChanges echouerait plutot que d'ecraser.
            await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            return new AcceptOfferResult(true, null, OfferView.From(dispatch, offre));
        }
        catch (DomainException exception) when (exception.Code is DispatchErrorCodes.AlreadyTaken
                                                    or DispatchErrorCodes.OfferExpired
                                                    or DispatchErrorCodes.DispatchClosed)
        {
            // CES TROIS-LA NE SONT PAS DES PANNES : la course est partie, ou
            // l'offre s'est eteinte. Le contrat prevoit un « rejection_code »
            // precisement pour les distinguer d'une erreur.
            return new AcceptOfferResult(false, exception.Code, null);
        }
        finally
        {
            await locks.ReleaseAsync(dispatch.DeliveryId, jeton, cancellationToken).ConfigureAwait(false);
        }
    }
}
