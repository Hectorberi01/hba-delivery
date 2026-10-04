using System.Globalization;
using Hba.BuildingBlocks.Application.Abstractions;
using Hba.BuildingBlocks.Application.Messaging;
using Hba.Payment.Application.Common.Interfaces;
using Hba.Payment.Domain.Payments;
using Hba.Payment.Domain.Refunds;
using Microsoft.Extensions.Logging;

namespace Hba.Payment.Application.Features.Refunds.Commands;

/// <summary>
/// Inscrit un remboursement a faire, parce qu'une mission payee s'est close sans
/// que le service soit rendu.
/// </summary>
///
/// <remarks>
/// DECLENCHEE PAR UN EVENEMENT, JAMAIS PAR UNE APPLICATION. C'est Payment qui sait
/// si l'argent est arrive : lui seul peut dire qu'il y a quelque chose a rendre.
///
/// LA COMMANDE PORTE LE FAIT METIER, PAS LE PRIX. L'appelant sait ce qui reste du
/// au livreur ; il ne sait PAS combien a ete encaisse — seule l'intention de paiement
/// le dit, et c'est ce gestionnaire qui la lit. Lui faire passer l'encaissement
/// l'obligerait a interroger Payment avant d'appeler Payment.
///
/// CE QUI A CHANGE LE 2 OCTOBRE 2026. Cette commande portait un booleen
/// « aucun service rendu » : vrai, tout etait du ; faux, le montant recommande
/// restait NUL — « a determiner » — parce que la politique d'annulation n'etait pas
/// tranchee. Un humain devait alors inventer un chiffre, livraison par livraison.
/// Le point 3 a tranche, la retenue est figee dans le devis, et le cas « a
/// determiner » DISPARAIT : il y a toujours un montant recommande.
/// </remarks>
public sealed record OpenRefundDueCommand(
    ServiceType ServiceType,
    Guid ReferenceId,
    string Reason,

    /// <summary>
    /// Ce qui reste au livreur, en francs CFA, et donc ce qui NE sera PAS rendu au
    /// client. Zero quand personne ne s'est deplace : tout l'encaissement est du.
    ///
    /// IL REMPLACE LE BOOLEEN « AUCUN SERVICE RENDU ». Zero dit ce que l'ancien
    /// « vrai » disait ; un montant dit ce que l'ancien « faux » ne savait pas
    /// dire.
    ///
    /// IL N'Y A PLUS DE CAS « A DETERMINER ». Les deux natures de mission portent
    /// leur retenue dans leurs evenements depuis le 2 octobre 2026 ; un montant
    /// recommande se calcule donc toujours, et la finance n'invente plus de chiffre.
    /// </summary>
    long CancellationFeeXof,

    DateTimeOffset OccurredAt) : ICommand<bool>;

public sealed class OpenRefundDueHandler(
    IRefundDueRepository refunds,
    IPaymentIntentRepository intents,
    IUnitOfWork unitOfWork,
    ILogger<OpenRefundDueHandler> logger) : ICommandHandler<OpenRefundDueCommand, bool>
{
    public async Task<bool> HandleAsync(OpenRefundDueCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        // LE PRIX DE LA MISSION, JAMAIS UN POURBOIRE. Un remboursement repond a une
        // course annulee ou sans chauffeur ; un pourboire laisse apres coup n'a
        // rien a voir avec cela, et le rendre automatiquement reprendrait au
        // livreur un argent qui lui etait destine.
        var intention = await intents
            .FindActiveByReferenceAsync(
                command.ServiceType,
                command.ReferenceId,
                PaymentPurpose.Mission,
                cancellationToken)
            .ConfigureAwait(false);

        // RIEN A RENDRE S'IL N'Y A RIEN A RENDRE, et c'est le cas le plus courant :
        // une course annulee avant d'etre payee, un paiement qui a echoue. Inscrire
        // une ligne de file pour chacune noierait la finance sous des taches vides.
        if (intention is null || intention.Status != PaymentStatus.Succeeded)
        {
            return false;
        }

        var deja = await refunds
            .FindByPaymentIntentAsync(intention.Id, cancellationToken)
            .ConfigureAwait(false);

        if (deja is not null)
        {
            // Le rejeu est le cas NORMAL : une course annulee puis declaree sans
            // chauffeur produit deux evenements pour un seul encaissement.
            return false;
        }

        refunds.Add(RefundDue.Open(
            Guid.CreateVersion7(),
            command.ServiceType,
            command.ReferenceId,
            intention.Id,
            intention.PayerId,
            intention.Amount.Amount,

            // LE MONTANT RECOMMANDE SE CALCULE ICI, parce que c'est ici qu'on a
            // l'intention sous les yeux : l'encaissement, moins ce qui reste au
            // livreur.
            //
            // LE PLANCHER A ZERO N'EST PAS DECORATIF. La retenue vient d'un autre
            // service, et rien ici ne garantit qu'elle a ete calculee sur CE
            // montant : un devis consomme puis un supplement facture a part
            // suffiraient a les desaccorder. Un recommande negatif se lirait a
            // l'ecran de la finance comme un remboursement a l'envers.
            Math.Max(0, intention.Amount.Amount - command.CancellationFeeXof),

            command.Reason,
            command.OccurredAt));

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // JOURNALISE EN Warning, PAS EN Information, ET C'EST VOULU. Ce n'est pas un
        // fonctionnement normal : quelqu'un a paye pour un service qu'il n'a pas eu,
        // et l'argent ne repart pas tout seul — FedaPay n'a pas d'API de
        // remboursement. Une ligne de file qui dort est de l'argent retenu.
        logger.LogWarning(
            "Remboursement a faire : mission {Nature} {ReferenceId}, {Montant} XOF encaisses, "
            + "motif {Motif}, recommande {Recommande}.",
            command.ServiceType,
            command.ReferenceId,
            intention.Amount.Amount,
            command.Reason,
            Math.Max(0, intention.Amount.Amount - command.CancellationFeeXof)
                .ToString(CultureInfo.InvariantCulture));

        return true;
    }
}
