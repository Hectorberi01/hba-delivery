using System.Globalization;
using Hba.BuildingBlocks.Domain;

namespace Hba.Billing.Domain.ValueObjects;

/// <summary>
/// Montant en francs CFA. Le XOF n'a pas de sous-unité : le montant est un
/// entier, et aucune opération ne doit introduire de décimale (ADR 0006).
/// </summary>
///
/// <remarks>
/// UNE COPIE DE PLUS, ET C'EST LA CONVENTION DU DÉPÔT. Payment, Pricing et
/// Delivery portent chacun le sien : un contexte borné ne partage pas ses
/// types de domaine avec un autre, sinon le jour où l'un d'eux doit changer
/// — un arrondi, une règle de signe — il les change tous.
///
/// CELUI-CI ADMET LE NÉGATIF, CONTRAIREMENT AUX AUTRES. Un solde postpayé
/// descend sous zéro et un mouvement de débit est un montant SIGNÉ : refuser
/// le négatif ici obligerait à porter le sens ailleurs, et la somme des
/// mouvements cesserait de faire le solde.
/// </remarks>
public sealed class MoneyXof : ValueObject, IComparable<MoneyXof>
{
    public const string CurrencyCode = "XOF";

    private MoneyXof(long amount) => Amount = amount;

    public long Amount { get; }

    /// <summary>
    /// UNE PROPRIÉTÉ, PAS UN CHAMP STATIQUE. EF Core suit les types possédés
    /// par référence : deux comptes qui partageraient la MÊME instance
    /// deviendraient le même enfant possédé de deux parents.
    /// </summary>
    public static MoneyXof Zero => new(0);

    public static MoneyXof From(long amount) => new(amount);

    /// <summary>Un montant de transaction : strictement positif.</summary>
    public static MoneyXof FromPositive(long amount)
    {
        if (amount <= 0)
        {
            throw new DomainException(
                Exceptions.BillingErrorCodes.InvalidAmount,
                "Un mouvement porte sur un montant strictement positif.");
        }

        return new MoneyXof(amount);
    }

    public static MoneyXof FromNonNegative(long amount)
    {
        if (amount < 0)
        {
            throw new DomainException(
                Exceptions.BillingErrorCodes.InvalidAmount,
                "Ce montant ne peut pas être négatif.");
        }

        return new MoneyXof(amount);
    }

    public MoneyXof Add(MoneyXof autre)
    {
        ArgumentNullException.ThrowIfNull(autre);
        return new MoneyXof(Amount + autre.Amount);
    }

    public MoneyXof Subtract(MoneyXof autre)
    {
        ArgumentNullException.ThrowIfNull(autre);
        return new MoneyXof(Amount - autre.Amount);
    }

    public MoneyXof Negate() => new(-Amount);

    public int CompareTo(MoneyXof? other) => other is null ? 1 : Amount.CompareTo(other.Amount);

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Amount;
    }

    public override string ToString() => $"{Amount.ToString(CultureInfo.InvariantCulture)} {CurrencyCode}";
}
