using Hba.BuildingBlocks.Application.Abstractions;
using Hba.BuildingBlocks.Application.Messaging;
using Hba.BuildingBlocks.Domain;
using Hba.Payment.Application.Common.Interfaces;
using Hba.Payment.Domain.Earnings;
using Microsoft.Extensions.Logging;

namespace Hba.Payment.Application.Features.Payouts.Commands;

/// <summary>
/// Le virement a eu lieu : on consigne sa reference, et LE GRAND LIVRE BOUGE.
///
/// C'EST LE SEUL GESTE DE TOUTE LA CHAINE QUI DEBITE LE COMPTE D'UN LIVREUR.
/// Pas la demande, pas l'approbation : celui-ci. La raison est simple — c'est
/// le seul moment ou quelqu'un affirme, reference en main, que l'argent est
/// parti. « Verse » doit vouloir dire « recu », sinon le releve cesse d'etre
/// une dette et devient une intention.
///
/// LES DEUX ECRITURES PARTENT ENSEMBLE OU PAS DU TOUT. Un seul
/// SaveChangesAsync porte la demande passee a « versee » ET la ligne de debit.
/// Les separer laisserait deux pannes possibles, et les deux sont graves dans
/// des sens opposes : une demande versee sans debit et le livreur peut
/// redemander la meme somme ; un debit sans demande versee et il a perdu de
/// l'argent que rien n'explique.
///
/// LE MONTANT DEBITE EST CELUI DE LA DEMANDE, PAS UN MONTANT SAISI. La finance
/// consigne une reference, pas une somme : autoriser un montant different ici
/// ouvrirait un ecart entre ce que le livreur avait demande et ce qu'on lui
/// retire, sans que rien dans le dossier ne le justifie. Un virement partiel
/// n'est pas prevu — s'il le devient un jour, ce sera une regle a trancher, pas
/// un champ a ajouter.
/// </summary>
public sealed record MarkPayoutPaidCommand(Guid PayoutId, string PaymentReference) : ICommand<PayoutRequestView>;

public sealed class MarkPayoutPaidHandler(
    IPayoutRequestRepository payouts,
    IDriverLedgerRepository ledger,
    IUnitOfWork unitOfWork,
    ICallerContext caller,
    IClock clock,
    ILogger<MarkPayoutPaidHandler> logger) : ICommandHandler<MarkPayoutPaidCommand, PayoutRequestView>
{
    public async Task<PayoutRequestView> HandleAsync(
        MarkPayoutPaidCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        PayoutReview.EnsureReviewer(caller);

        var demande = await payouts.GetByIdAsync(command.PayoutId, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException("Demande de versement", command.PayoutId.ToString());

        var signature = PayoutReview.Signature(caller);
        var maintenant = clock.UtcNow;

        // LA TRANSITION D'ABORD, L'ECRITURE ENSUITE. Si la demande n'est pas
        // approuvee, MarkPaid leve avant qu'aucune ligne n'ait ete preparee :
        // on ne construit pas un debit pour le jeter.
        demande.MarkPaid(command.PaymentReference, signature, maintenant);

        var debit = DriverLedgerEntry.ForPayout(
            Guid.CreateVersion7(),
            demande.DriverId,
            demande.AmountXof,

            // L'ORIGINE EST DANS LA LIGNE, et un index unique en base s'en
            // sert : deux requetes qui auraient charge la meme demande
            // approuvee avant que l'une n'enregistre ne pourront pas ecrire
            // deux debits.
            demande.Id,

            // LA DATE DU VIREMENT EST CELLE DE LA CONSIGNATION, faute de
            // mieux : l'operateur ne nous dit pas quand il a execute. C'est un
            // ecart assume, de quelques minutes a quelques heures, et il est
            // borne par le fait que la reference, elle, est exacte.
            maintenant);

        ledger.Add(debit);

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "Versement consigne : demande {PayoutId} de {AmountXof} F au livreur {DriverId}, " +
            "reference {Reference}, par {Acteur}. Debit {EntryId} ecrit au grand livre.",
            demande.Id,
            demande.AmountXof,
            demande.DriverId,
            demande.PaymentReference,
            caller.ToActor().ToString(),
            debit.Id);

        return PayoutReview.ToView(demande);
    }
}
