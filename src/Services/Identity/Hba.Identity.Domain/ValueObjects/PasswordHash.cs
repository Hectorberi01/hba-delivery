using System.Globalization;
using System.Security.Cryptography;
using Hba.BuildingBlocks.Domain;

namespace Hba.Identity.Domain.ValueObjects;

/// <summary>
/// Empreinte d'un mot de passe. PBKDF2-HMAC-SHA256, sel aléatoire de 16 octets,
/// clé de 32 octets. Le nombre d'itérations est stocké AVEC l'empreinte : le
/// jour où il faudra l'augmenter, les anciennes empreintes resteront
/// vérifiables et pourront être réécrites à la connexion suivante.
///
/// Rien ici ne dépend d'une bibliothèque externe : l'algorithme est dans la
/// bibliothèque standard, et le format est lisible.
/// </summary>
public sealed class PasswordHash : ValueObject
{
    public const int CurrentIterations = 210_000;
    private const int SaltSize = 16;
    private const int KeySize = 32;
    private const string Algorithm = "pbkdf2-sha256";
    private const int MinimumLength = 10;

    private PasswordHash(string encoded) => Encoded = encoded;

    /// <summary>Format : pbkdf2-sha256$itérations$sel$empreinte, en base64.</summary>
    public string Encoded { get; }

    /// <summary>Vrai si l'empreinte a été produite avec moins d'itérations qu'aujourd'hui.</summary>
    public bool NeedsRehash
    {
        get
        {
            var parts = Encoded.Split('$');
            return parts.Length != 4
                   || !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var iterations)
                   || iterations < CurrentIterations;
        }
    }

    public static PasswordHash FromPlainText(string? plainText)
    {
        if (string.IsNullOrWhiteSpace(plainText) || plainText.Length < MinimumLength)
        {
            throw new DomainException(
                "WEAK_PASSWORD",
                $"Le mot de passe doit faire au moins {MinimumLength} caractères.");
        }

        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var key = Rfc2898DeriveBytes.Pbkdf2(plainText, salt, CurrentIterations, HashAlgorithmName.SHA256, KeySize);

        var encoded = string.Join(
            '$',
            Algorithm,
            CurrentIterations.ToString(CultureInfo.InvariantCulture),
            Convert.ToBase64String(salt),
            Convert.ToBase64String(key));

        return new PasswordHash(encoded);
    }

    /// <summary>Reconstruction depuis la persistance.</summary>
    public static PasswordHash FromEncoded(string encoded)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(encoded);
        return new PasswordHash(encoded);
    }

    /// <summary>
    /// Comparaison à temps constant. Une empreinte illisible renvoie false au
    /// lieu de lever : une donnée corrompue ne doit pas devenir un moyen de
    /// distinguer un compte existant d'un compte inconnu.
    /// </summary>
    public bool Verify(string? candidate)
    {
        if (string.IsNullOrEmpty(candidate))
        {
            return false;
        }

        var parts = Encoded.Split('$');

        if (parts.Length != 4
            || !string.Equals(parts[0], Algorithm, StringComparison.Ordinal)
            || !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var iterations))
        {
            return false;
        }

        byte[] salt;
        byte[] expected;

        try
        {
            salt = Convert.FromBase64String(parts[2]);
            expected = Convert.FromBase64String(parts[3]);
        }
        catch (FormatException)
        {
            return false;
        }

        var actual = Rfc2898DeriveBytes.Pbkdf2(
            candidate,
            salt,
            iterations,
            HashAlgorithmName.SHA256,
            expected.Length);

        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Encoded;
    }
}
