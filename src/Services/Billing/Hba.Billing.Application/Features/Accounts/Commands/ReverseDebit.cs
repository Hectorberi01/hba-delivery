using Hba.Billing.Application.Authorization;
using Hba.Billing.Application.Common.Interfaces;
using Hba.Billing.Application.Common.Views;
using Hba.Billing.Domain.Accounts;
using Hba.Billing.Domain.Exceptions;
using Hba.BuildingBlocks.Application.Abstractions;
using Hba.BuildingBlocks.Application.Messaging;
using Hba.BuildingBlocks.Domain;

namespace Hba.Billing.Application.Features.Accounts.Commands;

/// <summary>
/// Annule un débit dont la contrepartie n'a pas abouti.
/// </summary>
///
/// <remarks>
/// LE DÉBIT ORPHELIN EST LE TROU QUE CETTE COMMANDE BOUCHE. Delivery débite
/// AVANT de créer la course, pour qu'un donneur d'ordre au plafond l'apprenne
/// avant d'avoir une course qui ne partira jamais. Le prix de ce choix : si
/// l'écriture de la course échoue ensuite, l'argent est parti et rien n'est
/// arrivé. Delivery appelle donc cette commande dans son rattrapage.
///
/// ON DÉSIGNE LE DÉBIT PAR SA CLÉ, PAS PAR SON IDENTIFIANT. L'appelant ne
/// connaît que ce qu'il a donné — l'identifiant de la course. Lui demander
/// l'identifiant du mouvement l'obligerait à le retenir entre deux appels, ce
/// qu'il ne peut pas faire justement dans le cas où tout échoue.
///
/// REJOUABLE SANS DOMMAGE. Deux annulations de la même course donnent le même
/// remboursement : la clé de l'annulation est dérivée de celle du débit, et
/// l'unicité en base fait le reste.
/// </remarks>
public sealed record ReverseDebitCommand(
    string OwnerType,
    string OwnerId,
    string DebitIdempotencyKey) : ICommand<MovementView>;

public sealed class ReverseDebitHandler(
    IBillingAccountRepository comptes,
    ITransactionRunner transactions,
    IUnitOfWork unitOfWork,
    ICallerContext caller,
    IClock clock) : ICommandHandler<ReverseDebitCommand, MovementView>
{
    public async Task<MovementView> HandleAsync(
        ReverseDebitCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        BillingAccess.EnsureCanReverse(caller, command.OwnerType, command.OwnerId);

        // PAS DE MAGASIN D'IDEMPOTENCE ICI, ET CE N'EST PAS UN OUBLI. La clé de
        // l'annulation se CALCULE à partir de celle du débit : on peut donc
        // chercher directement le remboursement lui-même, ce qui est plus sûr
        // qu'un magasin annexe — il ne peut pas être en désaccord avec les
        // écritures.
        var cle = AccountMovement.ReversalKey(command.DebitIdempotencyKey);

        var dejaRembourse = await comptes
            .FindMovementByKeyAsync(cle, cancellationToken)
            .ConfigureAwait(false);

        if (dejaRembourse is not null)
        {
            return MovementView.From(dejaRembourse);
        }

        var debit = await comptes
            .FindMovementByKeyAsync(command.DebitIdempotencyKey.Trim(), cancellationToken)
            .ConfigureAwait(false);

        if (debit is null)
        {
            // « INTROUVABLE » PLUTÔT QU'UN SUCCÈS VIDE. Le cas arrive pour de
            // bon : si le débit lui-même a échoué, Delivery n'a rien à annuler.
            // Mais rendre « rien » ferait passer pour normal le cas où la clé
            // est fausse — et une compensation qui ne compense rien en silence
            // est exactement ce qu'on ne veut pas ici.
            throw new DomainException(
                BillingErrorCodes.DebitNotFound,
                "Aucun débit ne porte cette référence : il n'y a rien à annuler.");
        }

        // MÊME VERROU QUE LE DÉBIT, DONC MÊME TRANSACTION : on relit et on
        // réécrit le solde.
        return await transactions.ExecuteAsync(
            async ct =>
            {
                var compte = await comptes
                    .GetForUpdateAsync(command.OwnerType, command.OwnerId, ct)
                    .ConfigureAwait(false)
                    ?? throw new NotFoundException(
                        "Compte de facturation",
                        $"{command.OwnerType}:{command.OwnerId}");

                var remboursement = compte.ReverseDebit(debit, caller.ToActor(), clock.UtcNow);

                comptes.AddMovement(remboursement);

                await unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);

                return MovementView.From(remboursement);
            },
            cancellationToken).ConfigureAwait(false);
    }
}
