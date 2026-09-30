using Hba.BuildingBlocks.Application.Abstractions;
using Hba.BuildingBlocks.Application.Messaging;
using Hba.BuildingBlocks.Domain;
using Hba.Billing.Application.Authorization;
using Hba.Billing.Application.Common.Interfaces;
using Hba.Billing.Application.Common.Views;
using Hba.Billing.Domain.Accounts;
using Hba.Billing.Domain.ValueObjects;

namespace Hba.Billing.Application.Features.Accounts.Commands;

/// <summary>
/// Crédite un compte : recharge, remboursement ou règlement de facture.
/// </summary>
///
/// <remarks>
/// UNE RECHARGE N'EST JAMAIS DÉCLENCHÉE PAR L'APPLICATION DU TITULAIRE. Elle
/// l'est par le webhook de l'opérateur, vérifié par Payment, et consommé ici
/// par l'Inbox — donc idempotent : un webhook rejoué ne crédite pas deux fois.
/// Créditer sur le retour d'écran donnerait du solde à qui n'a rien payé.
/// </remarks>
public sealed record CreditAccountCommand(
    string OwnerType,
    string OwnerId,
    MovementKind Kind,
    long AmountXof,
    string Reference,
    string IdempotencyKey) : ICommand<MovementView>;

public sealed class CreditAccountHandler(
    IBillingAccountRepository comptes,
    IIdempotencyStore idempotence,
    ITransactionRunner transactions,
    IUnitOfWork unitOfWork,
    ICallerContext caller,
    IClock clock) : ICommandHandler<CreditAccountCommand, MovementView>
{
    private const string Portee = "billing:credit";

    public async Task<MovementView> HandleAsync(
        CreditAccountCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        BillingAccess.EnsureCanCredit(caller);

        var connu = await idempotence
            .TryGetResultAsync(Portee, command.IdempotencyKey, cancellationToken)
            .ConfigureAwait(false);

        if (connu is not null && Guid.TryParse(connu, out var mouvementConnu))
        {
            var deja = await comptes.GetMovementAsync(mouvementConnu, cancellationToken).ConfigureAwait(false)
                ?? throw new NotFoundException("Mouvement", connu);

            return MovementView.From(deja);
        }

        // MÊME VERROU QUE LE DÉBIT, DONC MÊME TRANSACTION. Un crédit lit et
        // réécrit le même solde : s'en dispenser rendrait une recharge
        // concurrente d'une course capable d'écraser l'une des deux écritures.
        return await transactions.ExecuteAsync(
            async ct =>
            {
                var compte = await comptes
                    .GetForUpdateAsync(command.OwnerType, command.OwnerId, ct)
                    .ConfigureAwait(false)
                    ?? throw new NotFoundException(
                        "Compte de facturation",
                        $"{command.OwnerType}:{command.OwnerId}");

                var mouvement = compte.Credit(
                    command.Kind,
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
