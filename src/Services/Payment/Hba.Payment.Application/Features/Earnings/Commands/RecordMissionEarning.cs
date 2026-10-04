using Hba.BuildingBlocks.Application.Abstractions;
using Hba.BuildingBlocks.Application.Messaging;
using Hba.Payment.Application.Common.Interfaces;
using Hba.Payment.Domain.Earnings;
using Hba.Payment.Domain.Payments;
using Microsoft.Extensions.Logging;

namespace Hba.Payment.Application.Features.Earnings.Commands;

/// <summary>
/// Inscrit au compte du livreur ce que la course livree lui rapporte.
///
/// POURQUOI CETTE ECRITURE EXISTE. Jusqu'ici, « ce que le livreur a gagne »
/// n'existait nulle part : l'application additionnait les remunerations des
/// vingt-cinq dernieres courses qu'elle avait sous la main, et l'ecran devait
/// avouer que ce n'etait pas un solde. Un chiffre qui depend de la taille
/// d'une page n'est pas une dette ; celui-ci en est une.
///
/// LE MONTANT VIENT DE L'EVENEMENT, PAS D'UN CALCUL. « DeliveryCompleted »
/// porte la part du livreur telle que le devis l'a figee a la confirmation.
/// La recalculer ici ferait dependre la paie d'une grille qui a pu changer
/// depuis — alors que le livreur avait accepte sur la foi du premier chiffre.
///
/// RIEN NE SORT D'ICI. Aucun evenement n'est leve, aucun argent ne bouge :
/// c'est une ligne de grand livre, et rien de plus.
/// </summary>
public sealed record RecordMissionEarningCommand(
    ServiceType ServiceType,
    Guid ReferenceId,
    string DriverId,

    /// <summary>
    /// Reference lisible de la mission. VIDE POUR UNE COURSE DE PERSONNES : Ride
    /// n'attribue pas de numero, contrairement a Delivery. Le journal et le releve
    /// retombent alors sur l'identifiant — moins lisible, mais vrai.
    /// </summary>
    string ReferenceLabel,

    long AmountXof,
    DateTimeOffset OccurredAt) : ICommand<bool>;

public sealed class RecordMissionEarningHandler(
    IDriverLedgerRepository ledger,
    IUnitOfWork unitOfWork,
    ILogger<RecordMissionEarningHandler> logger) : ICommandHandler<RecordMissionEarningCommand, bool>
{
    public async Task<bool> HandleAsync(
        RecordMissionEarningCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (string.IsNullOrWhiteSpace(command.DriverId)
            || command.ReferenceId == Guid.Empty
            || command.ServiceType == ServiceType.Unspecified)
        {
            // UNE MISSION ACCOMPLIE SANS CHAUFFEUR N'EXISTE PAS, mais un evenement
            // mal forme, si. On le laisse passer sans ecrire plutot que de bloquer
            // la partition sur un message que personne ne reparera.
            logger.LogWarning(
                "Remuneration ignoree : mission {Nature} {ReferenceId}, chauffeur « {DriverId} ».",
                command.ServiceType,
                command.ReferenceId,
                command.DriverId);

            return false;
        }

        if (command.AmountXof < 0)
        {
            logger.LogWarning(
                "Remuneration negative ignoree pour la mission {Nature} {ReferenceId} : {Montant}.",
                command.ServiceType,
                command.ReferenceId,
                command.AmountXof);

            return false;
        }

        var existante = await ledger
            .FindMissionEntryAsync(
                LedgerEntryKind.DeliveryEarning,
                command.ServiceType,
                command.ReferenceId,
                cancellationToken)
            .ConfigureAwait(false);

        if (existante is not null)
        {
            // Le rejeu est le cas NORMAL d'un rattrapage de sujet, pas un
            // incident : on sort en silence.
            return false;
        }

        ledger.Add(DriverLedgerEntry.ForMission(
            Guid.CreateVersion7(),
            command.DriverId,
            command.AmountXof,
            command.ServiceType,
            command.ReferenceId,
            command.ReferenceLabel,
            command.OccurredAt));

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "Compte chauffeur {DriverId} credite de {Montant} XOF pour la mission {Nature} {Reference}.",
            command.DriverId,
            command.AmountXof,
            command.ServiceType,
            string.IsNullOrWhiteSpace(command.ReferenceLabel)
                ? command.ReferenceId.ToString()
                : command.ReferenceLabel);

        return true;
    }
}
