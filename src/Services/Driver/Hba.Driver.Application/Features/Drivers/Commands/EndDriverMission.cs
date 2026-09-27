using Hba.BuildingBlocks.Application.Abstractions;
using Hba.BuildingBlocks.Application.Messaging;
using Hba.BuildingBlocks.Domain;
using Hba.Driver.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;

namespace Hba.Driver.Application.Features.Drivers.Commands;

/// <summary>
/// La course est terminee : le livreur redevient disponible.
///
/// CE CHAINON MANQUAIT, ET SON ABSENCE NE SE VOYAIT PAS. DriverAggregate
/// portait CompleteMission depuis le premier jour ; personne ne l'appelait.
/// Un livreur qui livrait restait donc ON_MISSION pour toujours : invisible
/// pour toutes les vagues suivantes, et — c'est le symptome par lequel le
/// defaut s'est revele — incapable meme de se mettre hors ligne, puisque la
/// machine a etats l'interdit pendant une mission. Il fallait une ecriture en
/// base pour le liberer.
///
/// TROIS EVENEMENTS Y MENENT, PAS UN SEUL : livree, annulee, echouee. Ne
/// traiter que la remise laisserait le meme piege ouvert pour toute course
/// close autrement — et une annulation apres acceptation est exactement le
/// cas ou le livreur a le plus besoin d'etre rendu au systeme.
/// </summary>
public sealed record EndDriverMissionCommand(Guid DriverId, Actor Actor) : ICommand<bool>;

public sealed class EndDriverMissionHandler(
    IDriverRepository drivers,
    IUnitOfWork unitOfWork,
    IClock clock,
    ILogger<EndDriverMissionHandler> logger) : ICommandHandler<EndDriverMissionCommand, bool>
{
    public async Task<bool> HandleAsync(EndDriverMissionCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var driver = await drivers.GetByIdAsync(command.DriverId, cancellationToken).ConfigureAwait(false);

        if (driver is null)
        {
            logger.LogWarning("Fin de mission pour un livreur inconnu : {DriverId}.", command.DriverId);
            return false;
        }

        // CompleteMission SORT SANS RIEN FAIRE SI LE LIVREUR N'EST PAS EN
        // MISSION. C'est ce qui rend ce consommateur rejouable : Kafka livre
        // au moins une fois, et le rattrapage d'un sujet depuis son origine
        // fait repasser toutes les courses closes de l'historique.
        var avant = driver.OperationalStatus;
        driver.CompleteMission(command.Actor, clock.UtcNow);

        if (driver.OperationalStatus == avant)
        {
            return false;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "Livreur {DriverId} rendu disponible en fin de course, par {Acteur}.",
            command.DriverId,
            command.Actor.ToString());

        return true;
    }
}
