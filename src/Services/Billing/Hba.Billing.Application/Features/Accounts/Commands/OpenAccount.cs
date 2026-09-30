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
/// Ouvre le compte d'un donneur d'ordre. Toujours en prépayé.
/// </summary>
public sealed record OpenAccountCommand(
    string OwnerType,
    string OwnerId,
    long LowBalanceThresholdXof) : ICommand<BillingAccountView>;

public sealed class OpenAccountHandler(
    IBillingAccountRepository comptes,
    IUnitOfWork unitOfWork,
    ICallerContext caller,
    IClock clock) : ICommandHandler<OpenAccountCommand, BillingAccountView>
{
    public async Task<BillingAccountView> HandleAsync(
        OpenAccountCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        BillingAccess.EnsureCanOpen(caller);

        // UN SEUL COMPTE PAR TITULAIRE, ET ROUVRIR REND LE SIEN. Deux comptes
        // pour un même commerçant, ce sont deux soldes : le jour où l'un est à
        // sec et l'autre plein, personne ne saura lequel est le bon.
        var existant = await comptes
            .FindByOwnerAsync(command.OwnerType, command.OwnerId, cancellationToken)
            .ConfigureAwait(false);

        if (existant is not null)
        {
            return BillingAccountView.From(existant);
        }

        var compte = BillingAccount.Open(
            Guid.CreateVersion7(),
            command.OwnerType,
            command.OwnerId,
            MoneyXof.From(command.LowBalanceThresholdXof),
            caller.ToActor(),
            clock.UtcNow);

        comptes.Add(compte);

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return BillingAccountView.From(compte);
    }
}
