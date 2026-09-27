using System.Globalization;
using Hba.BuildingBlocks.Domain;

namespace Hba.Payment.Domain.ValueObjects;

/// <summary>
/// Montant en francs CFA. Le XOF n'a pas de sous-unite : le montant est un
/// entier, et aucune operation ne doit introduire de decimale (ADR 0006).
///
/// FEDAPAY ATTEND EXACTEMENT CA. Son champ <c>amount</c> est un entier de
/// l'unite monetaire, sans centimes : 1180 signifie 1180 FCFA. Il n'y a donc
/// aucune conversion a faire au passage de la frontiere, et surtout aucune
/// multiplication par cent a ne pas oublier.
/// </summary>
public sealed class MoneyXof : ValueObject, IComparable<MoneyXof>
{
    public const string CurrencyCode = "XOF";

    private MoneyXof(long amount) => Amount = amount;

    public long Amount { get; }

    /// <summary>
    /// UNE PROPRIETE, PAS UN CHAMP STATIQUE. EF Core suit les types possedes
    /// par reference : deux intentions qui partageraient la MEME instance
    /// deviendraient le meme enfant possede de deux parents, et SaveChanges
    /// echouerait sur la cle etrangere identifiante.
    /// </summary>
    public static MoneyXof Zero => new(0);

    public static MoneyXof From(long amount) => new(amount);

    public static MoneyXof FromPositive(long amount)
    {
        if (amount <= 0)
        {
            throw new DomainException("INVALID_AMOUNT", "Un paiement porte sur un montant strictement positif.");
        }

        return new MoneyXof(amount);
    }

    public int CompareTo(MoneyXof? other) => other is null ? 1 : Amount.CompareTo(other.Amount);

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Amount;
    }

    public override string ToString() => $"{Amount.ToString(CultureInfo.InvariantCulture)} {CurrencyCode}";
}
