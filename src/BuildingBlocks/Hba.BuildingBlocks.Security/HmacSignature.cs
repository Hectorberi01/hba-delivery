using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Hba.BuildingBlocks.Security;

/// <summary>
/// Signature HMAC des webhooks partenaires, et vérification de celle de FedaPay.
/// La comparaison est à temps constant.
/// </summary>
public static class HmacSignature
{
    /// <summary>
    /// Signe « timestamp.corps » en HMAC-SHA256. Le timestamp dans la charge
    /// signée empêche le rejeu d'un appel capturé.
    /// </summary>
    public static string Compute(string secret, string payload, DateTimeOffset timestamp)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(secret);

        var unixSeconds = timestamp.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        var signedPayload = $"{unixSeconds}.{payload}";

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(signedPayload));

        return $"t={unixSeconds},v1={Convert.ToHexString(hash).ToLowerInvariant()}";
    }

    /// <summary>
    /// Vérifie une signature au format « t=...,v1=... ». Rejette les signatures
    /// trop anciennes.
    /// </summary>
    public static bool Verify(string secret, string payload, string signatureHeader, TimeSpan tolerance)
    {
        if (string.IsNullOrWhiteSpace(signatureHeader))
        {
            return false;
        }

        string? timestampPart = null;
        string? signaturePart = null;

        foreach (var part in signatureHeader.Split(',', StringSplitOptions.TrimEntries))
        {
            if (part.StartsWith("t=", StringComparison.Ordinal))
            {
                timestampPart = part[2..];
            }
            else if (part.StartsWith("v1=", StringComparison.Ordinal))
            {
                signaturePart = part[3..];
            }
        }

        if (timestampPart is null
            || signaturePart is null
            || !long.TryParse(timestampPart, NumberStyles.Integer, CultureInfo.InvariantCulture, out var unixSeconds))
        {
            return false;
        }

        var timestamp = DateTimeOffset.FromUnixTimeSeconds(unixSeconds);
        if (DateTimeOffset.UtcNow - timestamp > tolerance)
        {
            return false;
        }

        var expected = Compute(secret, payload, timestamp);
        var expectedSignature = expected[(expected.IndexOf("v1=", StringComparison.Ordinal) + 3)..];

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expectedSignature),
            Encoding.UTF8.GetBytes(signaturePart));
    }
}
