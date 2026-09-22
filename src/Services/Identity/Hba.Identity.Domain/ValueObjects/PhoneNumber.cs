using System.Text.RegularExpressions;
using Hba.BuildingBlocks.Domain;

namespace Hba.Identity.Domain.ValueObjects;

/// <summary>
/// Numéro au format international. C'est l'identifiant principal des personnes :
/// il ne peut être ni vide, ni deviné. Aucun indicatif n'est ajouté
/// implicitement — un numéro sans « + » est refusé plutôt que complété.
/// </summary>
public sealed partial class PhoneNumber : ValueObject
{
    private PhoneNumber(string value) => Value = value;

    public string Value { get; }

    [GeneratedRegex(@"^\+[1-9]\d{7,14}$")]
    private static partial Regex E164();

    public static PhoneNumber Create(string? value)
    {
        var normalized = Normalize(value);

        if (!E164().IsMatch(normalized))
        {
            throw new DomainException(
                "INVALID_PHONE",
                "Numéro invalide. Format attendu : international, par exemple +22997000000.");
        }

        return new PhoneNumber(normalized);
    }

    public static bool IsValid(string? value) => !string.IsNullOrWhiteSpace(value) && E164().IsMatch(Normalize(value));

    private static string Normalize(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Replace(" ", string.Empty, StringComparison.Ordinal)
                   .Replace(".", string.Empty, StringComparison.Ordinal)
                   .Replace("-", string.Empty, StringComparison.Ordinal)
                   .Trim();

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => Value;
}
