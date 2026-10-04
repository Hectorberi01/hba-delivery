using Hba.BuildingBlocks.Application.Abstractions;
using Hba.BuildingBlocks.Application.Messaging;
using Hba.Payment.Application.Common.Interfaces;
using Hba.Payment.Domain.Earnings;
using Hba.Payment.Domain.Payments;
using Microsoft.Extensions.Logging;

namespace Hba.Payment.Application.Features.Earnings.Commands;

/// <summary>
/// Inscrit au compte du livreur ce que lui laisse une mission qui n'a pas abouti.
/// </summary>
///
/// <remarks>
/// DEUX SITUATIONS, UNE SEULE REGLE (points 3 et 34, tranches le 2 octobre 2026).
/// Le client annule apres qu'un livreur a accepte, ou le livreur declare un
/// incident et la mission part en echec : dans les deux cas il s'est deplace, dans
/// les deux cas il touche la meme chose. Jusqu'ici, ni l'un ni l'autre ne lui
/// rapportait quoi que ce soit.
///
/// LE MONTANT VIENT DE L'EVENEMENT, PAS D'UN CALCUL. La retenue est figee dans le
/// devis au taux de la grille du jour, exactement comme la part du livreur sur une
/// remise. Ce service n'a aucune grille a lire, et n'a pas le droit d'aller voir
/// celle de Pricing.
///
/// ELLE REVIENT EN ENTIER AU LIVREUR, sans part de HBA : la commission se prend
/// sur un service rendu, et il n'y en a pas eu.
///
/// RIEN NE SORT D'ICI. Aucun evenement, aucun argent qui bouge : une ligne de grand
/// livre, et rien de plus.
/// </remarks>
public sealed record RecordMissionCompensationCommand(
    ServiceType ServiceType,
    Guid ReferenceId,
    string DriverId,

    /// <summary>
    /// Reference lisible de la mission. VIDE POUR UNE COURSE DE PERSONNES : Ride
    /// n'attribue pas de numero, contrairement a Delivery.
    /// </summary>
    string ReferenceLabel,

    /// <summary>
    /// La retenue, en francs CFA. ZERO EST LE CAS COURANT, PAS UNE ERREUR : aucun
    /// livreur n'etait affecte, ou la grille ne prevoit pas de retenue. Le
    /// gestionnaire sort alors sans ecrire.
    /// </summary>
    long AmountXof,

    DateTimeOffset OccurredAt) : ICommand<bool>;

public sealed class RecordMissionCompensationHandler(
    IDriverLedgerRepository ledger,
    IUnitOfWork unitOfWork,
    ILogger<RecordMissionCompensationHandler> logger) : ICommandHandler<RecordMissionCompensationCommand, bool>
{
    public async Task<bool> HandleAsync(
        RecordMissionCompensationCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        // RIEN A DEDOMMAGER N'EST PAS UNE ANOMALIE, ET NE SE JOURNALISE PAS.
        //
        // C'est meme le cas le plus frequent : une course annulee avant qu'un
        // livreur accepte, ou une zone dont la grille laisse l'annulation gratuite.
        // Le consommateur appelle sans condition — tester la-bas lui demanderait de
        // connaitre la politique d'annulation, ce qui n'est pas son affaire — et
        // c'est ici qu'on sort. Un avertissement par annulation ordinaire noierait
        // les journaux sous du bruit.
        if (command.AmountXof <= 0)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(command.DriverId)
            || command.ReferenceId == Guid.Empty
            || command.ServiceType == ServiceType.Unspecified)
        {
            // UNE RETENUE SANS LIVREUR EST UNE CONTRADICTION, et elle se journalise
            // parce qu'elle revele un defaut en amont : le domaine ne doit renvoyer
            // un montant que lorsqu'un livreur etait affecte. On n'ecrit pas, et on
            // ne bloque pas la partition sur un message que personne ne reparera.
            logger.LogWarning(
                "Dedommagement de {Montant} XOF ignore : mission {Nature} {ReferenceId}, "
                + "livreur « {DriverId} ».",
                command.AmountXof,
                command.ServiceType,
                command.ReferenceId,
                command.DriverId);

            return false;
        }

        var existant = await ledger
            .FindMissionEntryAsync(
                LedgerEntryKind.Compensation,
                command.ServiceType,
                command.ReferenceId,
                cancellationToken)
            .ConfigureAwait(false);

        if (existant is not null)
        {
            // Le rejeu est le cas NORMAL d'un rattrapage de sujet. Il l'est encore
            // plus ici qu'ailleurs : une course annulee puis close de force par
            // l'exploitation produit DEUX evenements pour un seul deplacement.
            return false;
        }

        ledger.Add(DriverLedgerEntry.ForCompensation(
            Guid.CreateVersion7(),
            command.DriverId,
            command.AmountXof,
            command.ServiceType,
            command.ReferenceId,
            command.ReferenceLabel,
            command.OccurredAt));

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "Compte chauffeur {DriverId} credite de {Montant} XOF en dedommagement "
            + "de la mission {Nature} {Reference}, qui n'a pas abouti.",
            command.DriverId,
            command.AmountXof,
            command.ServiceType,
            string.IsNullOrWhiteSpace(command.ReferenceLabel)
                ? command.ReferenceId.ToString()
                : command.ReferenceLabel);

        return true;
    }
}
