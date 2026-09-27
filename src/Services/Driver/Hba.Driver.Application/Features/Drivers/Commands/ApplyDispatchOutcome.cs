using Hba.BuildingBlocks.Application.Abstractions;
using Hba.BuildingBlocks.Application.Messaging;
using Hba.BuildingBlocks.Domain;
using Hba.Driver.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;

namespace Hba.Driver.Application.Features.Drivers.Commands;

/// <summary>
/// Ce que le dispatch fait a l'etat du livreur.
///
/// TROIS TRANSITIONS, TOUTES DECLENCHEES PAR KAFKA. Le contrat de Driver
/// n'expose aucune methode de reservation, et ce n'est pas un oubli : le
/// commentaire de « OfferAccepted » dit que cet evenement est « consomme par
/// Delivery pour passer en DRIVER_ASSIGNED, ET PAR DRIVER pour passer le
/// livreur en ON_MISSION ». L'etat suit les evenements, il ne se pilote pas.
///
/// LA CONSEQUENCE EST ASSUMEE : la reservation est asynchrone. Entre l'envoi
/// d'une offre et l'arrivee de l'evenement, une recherche voisine peut encore
/// voir ce livreur disponible et lui proposer une seconde course. Il n'en
/// acceptera qu'une — le verrou du dispatch y veille — mais il pourra en voir
/// deux. Le corriger demanderait un appel synchrone que le contrat n'a pas.
/// </summary>
public enum DispatchOutcome
{
    /// <summary>Une offre part vers lui : il ne doit plus en recevoir d'autre.</summary>
    Reserved = 1,

    /// <summary>L'offre s'est eteinte — expiree, refusee, ou supplantee.</summary>
    Released = 2,

    /// <summary>Il a gagne la course.</summary>
    OnMission = 3,
}

public sealed record ApplyDispatchOutcomeCommand(Guid DriverId, DispatchOutcome Outcome) : ICommand<bool>;

public sealed class ApplyDispatchOutcomeHandler(
    IDriverRepository drivers,
    IUnitOfWork unitOfWork,
    IClock clock,
    ILogger<ApplyDispatchOutcomeHandler> logger) : ICommandHandler<ApplyDispatchOutcomeCommand, bool>
{
    public async Task<bool> HandleAsync(ApplyDispatchOutcomeCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var driver = await drivers.GetByIdAsync(command.DriverId, cancellationToken).ConfigureAwait(false);

        if (driver is null)
        {
            logger.LogWarning("Evenement de dispatch pour un livreur inconnu : {DriverId}.", command.DriverId);
            return false;
        }

        var acteur = Actor.DispatchEngine;
        var now = clock.UtcNow;

        try
        {
            switch (command.Outcome)
            {
                case DispatchOutcome.Reserved:
                    driver.Reserve(acteur, now);
                    break;

                case DispatchOutcome.Released:
                    driver.ReleaseReservation(acteur, now);
                    break;

                case DispatchOutcome.OnMission:
                    driver.StartMission(acteur, now);
                    break;

                default:
                    return false;
            }
        }
        catch (DomainException exception)
        {
            // UN REFUS DE TRANSITION N'EST PAS UNE PANNE DE CONSUMER. Les
            // evenements se rejouent et peuvent se croiser : reserver
            // quelqu'un deja reserve, liberer quelqu'un deja libre. Laisser
            // remonter l'exception bloquerait la partition Kafka sur un
            // message que personne ne pourra jamais traiter.
            logger.LogInformation(
                "Transition {Outcome} ignoree pour le livreur {DriverId} : {Raison}",
                command.Outcome,
                command.DriverId,
                exception.Message);

            return false;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }
}
