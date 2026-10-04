using System.Globalization;
using Hba.BuildingBlocks.Domain;

namespace Hba.Ride.Domain.ValueObjects;

/// <summary>
/// Un montant en francs CFA, en entier.
/// </summary>
///
/// <remarks>
/// ENTIER, PAS DÉCIMAL, ET AUCUNE DEVISE. L'ADR 0006 le fixe : le XOF n'a pas de
/// subdivision, et aucune opération ne doit introduire de décimale. Le point 35
/// a écarté le multi-devise le 1er octobre 2026 — d'où l'absence de champ
/// « Currency » sur la course : un champ qui ne peut prendre qu'une valeur est
/// une promesse de souplesse que rien ne tient, et il faudrait le remplir
/// partout pour rien.
///
/// C'EST UNE COPIE DE CELUI DE PRICING, comme celui de Payment en est une. Le
/// domaine ne partage pas ses objets de valeur entre services : chacun possède
/// les siens, sans quoi une modification en toucherait cinq.
/// </remarks>
public sealed class MoneyXof : ValueObject, IComparable<MoneyXof>
{
    public const string CurrencyCode = "XOF";

    private MoneyXof()
    {
    }

    private MoneyXof(long amount) => Amount = amount;

    public long Amount { get; private set; }

    public static MoneyXof Zero => new(0);

    public static MoneyXof From(long amount) => new(amount);

    public static bool EstValide(long amount) => amount >= 0;

    public int CompareTo(MoneyXof? other) => other is null ? 1 : Amount.CompareTo(other.Amount);

    public override string ToString()
        => $"{Amount.ToString(CultureInfo.InvariantCulture)} {CurrencyCode}";

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Amount;
    }
}
