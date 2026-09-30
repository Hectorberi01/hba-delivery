using Hba.BuildingBlocks.Application.Abstractions;
using Hba.BuildingBlocks.Application.Messaging;
using Hba.BuildingBlocks.Domain;
using Hba.Payment.Application.Common.Interfaces;
using Hba.Payment.Domain.Payments;
using Microsoft.Extensions.Logging;

namespace Hba.Payment.Application.Features.Payments.Commands;

/// <summary>
/// Ce qu'il est advenu d'une notification. Sert au point d'entree HTTP a
/// journaliser sans interpreter, et a repondre 2xx dans tous les cas ou la
/// signature etait bonne.
/// </summary>
public enum ProviderOutcome
{
    /// <summary>Aucune intention ne porte cette reference.</summary>
    Unknown = 0,

    /// <summary>La transaction est toujours en cours chez le fournisseur.</summary>
    StillPending = 1,

    Succeeded = 2,

    Failed = 3,

    /// <summary>
    /// Le fournisseur a rendu l'argent. CONSTATE, PAS DEMANDE PAR NOUS.
    /// </summary>
    Refunded = 4,
}

/// <summary>
/// Application du verdict du fournisseur sur une intention.
///
/// LE WEBHOOK NE PORTE QU'UNE REFERENCE, jamais un verdict. Sa signature prouve
/// qui l'envoie ; elle ne prouve pas que son corps decrit encore l'etat courant
/// — deux notifications peuvent se croiser, et le fournisseur lui-meme
/// recommande de relire la transaction par l'API. On relit donc, toujours.
/// </summary>
public sealed record ApplyProviderOutcomeCommand(string ProviderReference) : ICommand<ProviderOutcome>;

public sealed class ApplyProviderOutcomeHandler(
    IPaymentIntentRepository intents,
    IPaymentProvider provider,
    IUnitOfWork unitOfWork,
    IClock clock,
    ILogger<ApplyProviderOutcomeHandler> logger) : ICommandHandler<ApplyProviderOutcomeCommand, ProviderOutcome>
{
    public async Task<ProviderOutcome> HandleAsync(
        ApplyProviderOutcomeCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var intent = await intents
            .FindByProviderReferenceAsync(command.ProviderReference, cancellationToken)
            .ConfigureAwait(false);

        if (intent is null)
        {
            // CE N'EST PAS FORCEMENT UNE ATTAQUE. La signature etait valide :
            // c'est bien le fournisseur qui parle. Une transaction ouverte par
            // un autre environnement partageant le meme compte, ou creee a la
            // main dans le tableau de bord, arrive exactement comme ca.
            logger.LogWarning(
                "Notification de paiement pour une reference inconnue : {Reference}.",
                command.ProviderReference);

            return ProviderOutcome.Unknown;
        }

        var payment = await provider
            .GetPaymentAsync(command.ProviderReference, cancellationToken)
            .ConfigureAwait(false);

        var actor = Actor.FedaPay;
        var now = clock.UtcNow;

        switch (payment.Status)
        {
            case PaymentStatus.Succeeded:
                intent.MarkSucceeded(actor, now);
                break;

            case PaymentStatus.Failed:
                intent.MarkFailed(payment.FailureReason ?? payment.Raw, actor, now);
                break;

            // UN REMBOURSEMENT TOMBAIT DANS LE « default », ET Y ETAIT
            // JOURNALISE « TOUJOURS EN COURS CHEZ LE FOURNISSEUR ».
            //
            // C'etait faux, et c'etait le seul endroit ou le systeme aurait pu
            // l'apprendre. FedaPay n'a pas d'API de remboursement — verifie le
            // 30 septembre 2026 : tableau de bord uniquement, MTN Mobile Money
            // seulement — donc un remboursement est un geste humain chez le
            // fournisseur. La seule facon de le savoir est celle-ci : le webhook
            // arrive, on relit la transaction comme toujours, et son statut a
            // change.
            //
            // RESTE A VERIFIER SUR LE COMPTE MARCHAND si FedaPay notifie bien un
            // remboursement fait au tableau de bord. S'il ne notifie pas, ce
            // chemin ne sera jamais emprunte et il faudra une relecture
            // periodique des intentions payees. Le code est juste dans les deux
            // cas ; c'est sa mise en mouvement qui depend du fournisseur.
            case PaymentStatus.Refunded:
            case PaymentStatus.PartiallyRefunded:
                intent.MarkRefunded(
                    partiel: payment.Status == PaymentStatus.PartiallyRefunded,
                    actor,
                    now);
                break;

            default:
                // Rien a faire, et surtout rien a inventer : une transaction
                // encore en cours reste en cours. Le fournisseur rappellera.
                logger.LogInformation(
                    "Transaction {Reference} toujours en cours chez le fournisseur ({Raw}).",
                    command.ProviderReference,
                    payment.Raw);

                return ProviderOutcome.StillPending;
        }

        // Rien n'est ecrit si l'agregat n'a pas bouge : un rejeu du webhook
        // repasse ici, ne change aucun etat, ne leve aucun evenement, et rend
        // le meme verdict.
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return payment.Status switch
        {
            PaymentStatus.Succeeded => ProviderOutcome.Succeeded,
            PaymentStatus.Refunded or PaymentStatus.PartiallyRefunded => ProviderOutcome.Refunded,
            _ => ProviderOutcome.Failed,
        };
    }
}
