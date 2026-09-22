using System.Text.RegularExpressions;

namespace Hba.Directory.Domain.ValueObjects;

/// <summary>
/// Validation d'un numéro au format international. Dupliquée depuis les autres
/// services à dessein : chaque service possède son modèle, et aucun ne dépend
/// du domaine d'un autre.
/// </summary>
public static partial class PhoneNumber
{
    [GeneratedRegex(@"^\+[1-9]\d{7,14}$")]
    private static partial Regex E164();

    public static bool IsValid(string? value)
        => !string.IsNullOrWhiteSpace(value) && E164().IsMatch(Normalize(value));

    public static string Normalize(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Replace(" ", string.Empty, StringComparison.Ordinal)
                   .Replace(".", string.Empty, StringComparison.Ordinal)
                   .Replace("-", string.Empty, StringComparison.Ordinal)
                   .Trim();
}
