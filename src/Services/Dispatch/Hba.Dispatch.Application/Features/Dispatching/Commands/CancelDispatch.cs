using Hba.BuildingBlocks.Application.Abstractions;
using Hba.BuildingBlocks.Application.Messaging;
using Hba.Dispatch.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;

namespace Hba.Dispatch.Application.Features.Dispatching.Commands;

/// <summary>
/// La course est close ailleurs : le moteur cesse de chercher.
///
/// SANS CETTE COMMANDE, DEUX CHOSES CASSENT. Le moteur continuerait a
/// proposer une course annulee a des livreurs, qui se deplaceraient pour
/// rien ; et chaque offre envoyee ferait echouer le consumer de Delivery, qui
/// refuserait la transition vers « recherche en cours » depuis un etat
/// terminal — bloquant la partition Kafka sur un message que personne ne
/// pourra jamais traiter.
/// </summary>
public sealed record CancelDispatchCommand(Guid DeliveryId, string Raison) : ICommand<bool>;

public sealed class CancelDispatchHandler(
    IDispatchRepository dispatches,
    IUnitOfWork unitOfWork,
    IClock clock,
    ILogger<CancelDispatchHandler> logger) : ICommandHandler<CancelDispatchCommand, bool>
{
    public async Task<bool> HandleAsync(CancelDispatchCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var dispatch = await dispatches
            .GetByDeliveryIdAsync(command.DeliveryId, cancellationToken)
            .ConfigureAwait(false);

        if (dispatch is null)
        {
            return false;
        }

        // Cancel est sans effet sur une recherche deja close ou deja
        // attribuee : une course annulee APRES affectation se regle entre
        // Delivery et le livreur, pas ici.
        dispatch.Cancel(clock.UtcNow);

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "Recherche close pour la livraison {DeliveryId} : {Raison}.",
            command.DeliveryId,
            command.Raison);

        return true;
    }
}
