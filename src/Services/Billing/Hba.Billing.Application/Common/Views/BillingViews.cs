using Hba.Billing.Domain.Accounts;

namespace Hba.Billing.Application.Common.Views;

/// <summary>
/// Ce qu'un titulaire et le back-office ont le droit de savoir d'un compte.
/// </summary>
///
/// <remarks>
/// « Available » EST RENDU PARCE QUE C'EST LA SEULE CHOSE QUI RÉPOND À LA
/// QUESTION POSÉE. Un titulaire ne demande pas son solde pour le plaisir : il
/// demande s'il peut commander. En prépayé les deux se confondent ; en
/// postpayé, un solde de −40 000 avec un plafond de 50 000 veut dire qu'il
/// reste 10 000, et laisser l'application faire l'addition, c'est accepter
/// qu'elle la fasse un jour de travers.
/// </remarks>
public sealed record BillingAccountView(
    Guid Id,
    string OwnerType,
    string OwnerId,
    SettlementMode Mode,
    long BalanceXof,
    long CreditLimitXof,
    long AvailableXof,
    long LowBalanceThresholdXof,
    AccountStatus Status)
{
    public static BillingAccountView From(BillingAccount compte)
    {
        ArgumentNullException.ThrowIfNull(compte);

        return new BillingAccountView(
            compte.Id,
            compte.OwnerType,
            compte.OwnerId,
            compte.SettlementMode,
            compte.Balance.Amount,
            compte.CreditLimit.Amount,
            compte.Available.Amount,
            compte.LowBalanceThreshold.Amount,
            compte.Status);
    }
}

public sealed record MovementView(
    Guid Id,
    Guid AccountId,
    MovementKind Kind,
    long AmountXof,
    long BalanceAfterXof,
    string Reference,
    DateTimeOffset CreatedAt)
{
    public static MovementView From(AccountMovement mouvement)
    {
        ArgumentNullException.ThrowIfNull(mouvement);

        return new MovementView(
            mouvement.Id,
            mouvement.AccountId,
            mouvement.Kind,
            mouvement.Amount.Amount,
            mouvement.BalanceAfter.Amount,
            mouvement.Reference,
            mouvement.CreatedAt);
    }
}
