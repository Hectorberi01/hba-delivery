using System.Globalization;
using Hba.BuildingBlocks.Domain;

namespace Hba.Pricing.Domain.ValueObjects;

/// <summary>
/// Montant en francs CFA. Copie locale du type de Delivery : un service ne
/// partage pas son domaine avec un autre. Le XOF n'a pas de sous-unité, donc
/// aucun calcul ne doit produire de décimale.
/// </summary>
public sealed class MoneyXof : ValueObject, IComparable<MoneyXof>
{
    public const string CurrencyCode = "XOF";

    private MoneyXof(long amount) => Amount = amount;

    public long Amount { get; }

    /// <summary>
    /// Une propriété, pas un champ statique : un objet-valeur cartographié par
    /// EF Core ne doit jamais être partagé par référence entre deux parents.
    /// </summary>
    public static MoneyXof Zero => new(0);

    public static MoneyXof From(long amount) => new(amount);

    public static MoneyXof FromNonNegative(long amount)
    {
        if (amount < 0)
        {
            throw new DomainException("INVALID_AMOUNT", "Un montant tarifaire ne peut pas être négatif.");
        }

        return new MoneyXof(amount);
    }

    public MoneyXof Add(MoneyXof other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return new MoneyXof(Amount + other.Amount);
    }

    /// <summary>Multiplication par un taux en points de base : 12500 vaut 1,25.</summary>
    public MoneyXof ApplyBasisPoints(int basisPoints)
    {
        if (basisPoints < 0)
        {
            throw new DomainException("INVALID_RATE", "Un taux ne peut pas être négatif.");
        }

        return new MoneyXof(Amount * basisPoints / 10_000);
    }

    /// <summary>Arrondi à la dizaine de francs supérieure, usage local des prix affichés.</summary>
    public MoneyXof RoundUpToTen()
    {
        var remainder = Amount % 10;
        return remainder == 0 ? this : new MoneyXof(Amount + (10 - remainder));
    }

    /// <summary>Arrondi à la dizaine inférieure : sert à la part du livreur, qui ne doit jamais dépasser le total.</summary>
    public MoneyXof RoundDownToTen() => new(Amount - (Amount % 10));

    public int CompareTo(MoneyXof? other) => other is null ? 1 : Amount.CompareTo(other.Amount);

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Amount;
    }

    public override string ToString() => $"{Amount.ToString(CultureInfo.InvariantCulture)} {CurrencyCode}";
}
