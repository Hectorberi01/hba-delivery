using System.Globalization;
using Hba.BuildingBlocks.Domain;

namespace Hba.Delivery.Domain.ValueObjects;

/// <summary>
/// Montant en francs CFA. Le XOF n'a pas de sous-unité : le montant est un
/// entier, et aucune opération ne doit introduire de décimale. Les arrondis se
/// font à la dizaine supérieure, conformément à l'usage local des prix affichés.
/// </summary>
public sealed class MoneyXof : ValueObject, IComparable<MoneyXof>
{
    public const string CurrencyCode = "XOF";

    private MoneyXof(long amount) => Amount = amount;

    public long Amount { get; }

    public static readonly MoneyXof Zero = new(0);

    public static MoneyXof From(long amount) => new(amount);

    public static MoneyXof FromNonNegative(long amount)
    {
        if (amount < 0)
        {
            throw new DomainException("INVALID_AMOUNT", "Un montant de livraison ne peut pas être négatif.");
        }

        return new MoneyXof(amount);
    }

    public MoneyXof Add(MoneyXof other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return new MoneyXof(Amount + other.Amount);
    }

    public MoneyXof Subtract(MoneyXof other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return new MoneyXof(Amount - other.Amount);
    }

    /// <summary>Arrondi à la dizaine de francs supérieure.</summary>
    public MoneyXof RoundUpToTen()
    {
        var remainder = Amount % 10;
        return remainder == 0 ? this : new MoneyXof(Amount + (10 - remainder));
    }

    public int CompareTo(MoneyXof? other) => other is null ? 1 : Amount.CompareTo(other.Amount);

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Amount;
    }

    public override string ToString() => $"{Amount.ToString(CultureInfo.InvariantCulture)} {CurrencyCode}";
}
