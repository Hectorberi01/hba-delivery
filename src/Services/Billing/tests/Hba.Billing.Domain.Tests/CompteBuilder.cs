using Hba.BuildingBlocks.Domain;
using Hba.Billing.Domain.Accounts;
using Hba.Billing.Domain.ValueObjects;

namespace Hba.Billing.Domain.Tests;

/// <summary>
/// De quoi écrire un test en une ligne. Les valeurs par défaut sont ARBITRAIRES
/// et n'engagent rien : la grille commerciale n'est pas tranchée, et un test
/// qui reprendrait des montants « réalistes » laisserait croire le contraire.
/// </summary>
internal static class CompteBuilder
{
    public static readonly Actor Finance = Actor.Admin("finance-1");

    public static DateTimeOffset At(int minutes = 0)
        => new DateTimeOffset(2026, 9, 29, 8, 0, 0, TimeSpan.Zero).AddMinutes(minutes);

    public static BillingAccount Ouvert(long seuil = 1_000)
        => BillingAccount.Open(
            Guid.CreateVersion7(),
            "merchant",
            "merchant-1",
            MoneyXof.From(seuil),
            Finance,
            At());

    /// <summary>Un compte prépayé déjà rechargé.</summary>
    public static BillingAccount Approvisionne(long montant, long seuil = 1_000)
    {
        var compte = Ouvert(seuil);
        compte.Credit(MovementKind.Topup, MoneyXof.From(montant), "recharge-1", "cle-recharge-1", Finance, At());
        compte.ClearDomainEvents();
        return compte;
    }
}
