using System.Globalization;
using Hba.BuildingBlocks.Domain;
using Hba.Billing.Domain.Exceptions;
using Hba.Billing.Domain.ValueObjects;

namespace Hba.Billing.Domain.Accounts;

/// <summary>
/// Un mouvement du compte. La vérité comptable, en AJOUT SEULEMENT.
/// </summary>
///
/// <remarks>
/// LE SOLDE DU COMPTE EST UN CACHE, CES LIGNES SONT LA VÉRITÉ. La somme des
/// montants doit refaire le solde, à tout instant. C'est ce qui permettra à un
/// contrôle périodique de dire qu'un compte est juste — et sans lui, un compte
/// est invérifiable, ce qui est une façon polie de dire qu'on ne saura jamais
/// s'il est faux.
///
/// LE MONTANT EST SIGNÉ : positif au crédit, négatif au débit. Porter le sens
/// dans le seul <see cref="MovementKind"/> obligerait chaque lecteur à
/// connaître la table des natures pour additionner deux lignes — et le premier
/// qui l'oublierait ferait une somme fausse sans que rien ne le signale.
///
/// <see cref="BalanceAfter"/> EST ÉCRIT, PAS RECALCULÉ. Il fige le solde tel
/// qu'il était après ce mouvement : c'est ce qui rend un relevé relisible des
/// mois plus tard, et ce qui permet de localiser l'écart quand la somme ne
/// tombe plus juste.
///
/// LA CLÉ D'IDEMPOTENCE EST PORTÉE ICI, MAIS ELLE N'EST PAS GARANTIE ICI.
/// C'est une contrainte d'unicité en base qui interdit le double débit ; le
/// domaine ne voit qu'un mouvement à la fois et ne peut rien promettre. Écrire
/// une vérification en mémoire donnerait l'illusion d'une protection, et cette
/// illusion est pire que l'absence.
/// </remarks>
public sealed class AccountMovement : Entity
{
    private AccountMovement()
    {
    }

    private AccountMovement(
        Guid id,
        Guid accountId,
        MovementKind kind,
        MoneyXof amount,
        MoneyXof balanceAfter,
        string reference,
        string idempotencyKey,
        DateTimeOffset createdAt)
        : base(id)
    {
        AccountId = accountId;
        Kind = kind;
        Amount = amount;
        BalanceAfter = balanceAfter;
        Reference = reference;
        IdempotencyKey = idempotencyKey;
        CreatedAt = createdAt;
    }

    public Guid AccountId { get; private set; }

    public MovementKind Kind { get; private set; }

    /// <summary>Montant signé : positif au crédit, négatif au débit.</summary>
    public MoneyXof Amount { get; private set; } = null!;

    public MoneyXof BalanceAfter { get; private set; } = null!;

    /// <summary>Course, intention de paiement ou facture à l'origine du mouvement.</summary>
    public string Reference { get; private set; } = string.Empty;

    public string IdempotencyKey { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>
    /// La clé d'idempotence de l'annulation d'un débit, dérivée de la sienne.
    /// </summary>
    ///
    /// <remarks>
    /// DÉRIVÉE, ET NON TIRÉE AU HASARD : c'est ce qui rend l'annulation
    /// rejouable. Delivery n'a qu'un identifiant de course à donner ; si la
    /// compensation était appelée deux fois — une fois par le gestionnaire, une
    /// fois par un balayage — la seconde doit retrouver le remboursement déjà
    /// écrit et non en faire un second. L'unicité en base la garantit, à
    /// condition que la clé soit CALCULABLE.
    /// </remarks>
    public static string ReversalKey(string debitIdempotencyKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(debitIdempotencyKey);

        return $"reverse:{debitIdempotencyKey.Trim()}";
    }

    /// <summary>
    /// Fabrique le mouvement. RÉSERVÉE À L'AGRÉGAT : c'est lui qui connaît le
    /// solde et qui décide si l'écriture est permise.
    /// </summary>
    internal static AccountMovement Create(
        Guid accountId,
        MovementKind kind,
        MoneyXof signedAmount,
        MoneyXof balanceAfter,
        string reference,
        string idempotencyKey,
        DateTimeOffset createdAt)
    {
        ArgumentNullException.ThrowIfNull(signedAmount);
        ArgumentNullException.ThrowIfNull(balanceAfter);

        if (string.IsNullOrWhiteSpace(reference))
        {
            throw new DomainException(
                BillingErrorCodes.MissingReference,
                "Un mouvement porte toujours la référence de ce qui l'a provoqué.");
        }

        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new DomainException(
                BillingErrorCodes.MissingIdempotencyKey,
                "Un mouvement porte toujours une clé d'idempotence.");
        }

        // LE SENS DOIT S'ACCORDER AVEC LA NATURE, sauf pour l'ajustement qui
        // corrige dans les deux sens. Un « topup » négatif est un bug de
        // l'appelant, et le laisser passer produirait une ligne que personne
        // ne saurait relire.
        var attenduPositif = kind is MovementKind.Topup or MovementKind.Refund or MovementKind.InvoicePayment;
        var attenduNegatif = kind is MovementKind.Debit;

        if (attenduPositif && signedAmount.Amount <= 0)
        {
            throw new DomainException(
                BillingErrorCodes.InvalidAmount,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Un mouvement de type {kind} est un crédit : son montant doit être positif."));
        }

        if (attenduNegatif && signedAmount.Amount >= 0)
        {
            throw new DomainException(
                BillingErrorCodes.InvalidAmount,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Un mouvement de type {kind} est un débit : son montant doit être négatif."));
        }

        if (kind == MovementKind.Adjustment && signedAmount.Amount == 0)
        {
            throw new DomainException(
                BillingErrorCodes.InvalidAmount,
                "Un ajustement nul ne corrige rien.");
        }

        return new AccountMovement(
            Guid.CreateVersion7(),
            accountId,
            kind,
            signedAmount,
            balanceAfter,
            reference.Trim(),
            idempotencyKey.Trim(),
            createdAt);
    }
}
