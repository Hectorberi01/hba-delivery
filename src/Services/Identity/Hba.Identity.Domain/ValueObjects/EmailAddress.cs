using System.Text.RegularExpressions;
using Hba.BuildingBlocks.Domain;

namespace Hba.Identity.Domain.ValueObjects;

/// <summary>
/// Adresse e-mail, normalisée en minuscules. Utilisée par le portail commerçant
/// et le back-office, jamais par les applications mobiles.
/// </summary>
public sealed partial class EmailAddress : ValueObject
{
    private EmailAddress(string value) => Value = value;

    public string Value { get; }

    [GeneratedRegex(@"^[^@\s]+@[^@\s.]+(\.[^@\s.]+)+$")]
    private static partial Regex Pattern();

    public static EmailAddress Create(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant() ?? string.Empty;

        if (!Pattern().IsMatch(normalized))
        {
            throw new DomainException("INVALID_EMAIL", $"Adresse e-mail invalide : {value}.");
        }

        return new EmailAddress(normalized);
    }

    public static bool IsValid(string? value)
        => !string.IsNullOrWhiteSpace(value) && Pattern().IsMatch(value.Trim().ToLowerInvariant());

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => Value;
}
