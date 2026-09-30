using Hba.BuildingBlocks.Application.Abstractions;
using Hba.BuildingBlocks.Application.Messaging;
using Hba.BuildingBlocks.Domain;
using Hba.Billing.Application.Authorization;
using Hba.Billing.Application.Common.Interfaces;
using Hba.Billing.Application.Common.Views;
using Hba.Billing.Domain.ValueObjects;

namespace Hba.Billing.Application.Features.Accounts.Commands;

/// <summary>
/// Débite une course du compte d'un donneur d'ordre.
/// </summary>
///
/// <remarks>
/// LA CLÉ D'IDEMPOTENCE EST L'IDENTIFIANT DE LA COURSE, tiré par Delivery AVANT
/// l'appel. C'est elle, et elle seule, qui empêche qu'un rejeu de
/// <c>CreateDelivery</c> débite deux fois.
/// </remarks>
public sealed record DebitAccountCommand(
    string OwnerType,
    string OwnerId,
    long AmountXof,
    string Reference,
    string IdempotencyKey) : ICommand<MovementView>;

public sealed class DebitAccountHandler(
    IBillingAccountRepository comptes,
    IIdempotencyStore idempotence,
    ITransactionRunner transactions,
    IUnitOfWork unitOfWork,
    ICallerContext caller,
    IClock clock) : ICommandHandler<DebitAccountCommand, MovementView>
{
    private const string Portee = "billing:debit";

    public async Task<MovementView> HandleAsync(
        DebitAccountCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        // L'AUTORISATION PASSE AVANT LE REJEU, ET NON APRÈS. Rendre le mouvement
        // déjà écrit à qui n'a pas le droit de débiter ce compte livrerait un
        // solde et une référence de course à un tiers : le rejeu est une lecture
        // comme une autre.
        BillingAccess.EnsureCanDebit(caller, command.OwnerType, command.OwnerId);

        // LE REJEU EST TRAITÉ AVANT TOUT, ET SANS VERROU. Un appel qui a déjà
        // débité n'a aucune raison de faire attendre les autres sur la ligne du
        // compte : on rend le mouvement déjà écrit et on s'arrête là.
        var connu = await idempotence
            .TryGetResultAsync(Portee, command.IdempotencyKey, cancellationToken)
            .ConfigureAwait(false);

        if (connu is not null && Guid.TryParse(connu, out var mouvementConnu))
        {
            var deja = await comptes.GetMovementAsync(mouvementConnu, cancellationToken).ConfigureAwait(false)
                ?? throw new NotFoundException("Mouvement", connu);

            return MovementView.From(deja);
        }

        // LA TRANSACTION S'OUVRE AVANT LE VERROU, ET C'EST CE QUI MANQUAIT.
        //
        // Le « FOR UPDATE » de GetForUpdateAsync ne vaut QUE dans une
        // transaction : sans elle, PostgreSQL le relâche à la fin de
        // l'instruction et la sérialisation disparaît sans bruit. Personne n'en
        // ouvrait — pas même ce gestionnaire, qui se contentait d'un
        // SaveChangesAsync à la fin. Deux courses du même donneur d'ordre à la
        // même milliseconde passaient donc toutes les deux le plafond.
        //
        // TOUT EST DEDANS : le verrou, la règle de solde, l'écriture du
        // mouvement, la clé d'idempotence et les messages d'Outbox. Ou tout est
        // validé, ou rien ne l'est — un solde débité sans mouvement en face
        // serait un compte faux que rien ne permettrait de retrouver.
        return await transactions.ExecuteAsync(
            async ct =>
            {
                var compte = await comptes
                    .GetForUpdateAsync(command.OwnerType, command.OwnerId, ct)
                    .ConfigureAwait(false)
                    ?? throw new NotFoundException(
                        "Compte de facturation",
                        $"{command.OwnerType}:{command.OwnerId}");

                var mouvement = compte.Debit(
                    MoneyXof.From(command.AmountXof),
                    command.Reference,
                    command.IdempotencyKey,
                    caller.ToActor(),
                    clock.UtcNow);

                comptes.AddMovement(mouvement);

                await idempotence
                    .RememberAsync(Portee, command.IdempotencyKey, mouvement.Id.ToString(), ct)
                    .ConfigureAwait(false);

                await unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);

                return MovementView.From(mouvement);
            },
            cancellationToken).ConfigureAwait(false);
    }
}
