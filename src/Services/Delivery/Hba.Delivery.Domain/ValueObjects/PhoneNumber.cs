using System.Text.RegularExpressions;

namespace Hba.Delivery.Domain.ValueObjects;

/// <summary>
/// Validation minimale d'un numéro au format international. Le téléphone est
/// l'identifiant principal des personnes : il ne peut pas être approximatif.
/// </summary>
public static partial class PhoneNumber
{
    [GeneratedRegex(@"^\+[1-9]\d{7,14}$")]
    private static partial Regex E164();

    public static bool IsValid(string? value)
        => !string.IsNullOrWhiteSpace(value) && E164().IsMatch(Normalize(value));

    /// <summary>Retire espaces, points et tirets. Ne devine jamais l'indicatif.</summary>
    public static string Normalize(string value)
        => string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Replace(" ", string.Empty, StringComparison.Ordinal)
                   .Replace(".", string.Empty, StringComparison.Ordinal)
                   .Replace("-", string.Empty, StringComparison.Ordinal)
                   .Trim();
}
