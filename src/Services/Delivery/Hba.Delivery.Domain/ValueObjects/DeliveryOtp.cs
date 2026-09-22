using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Hba.BuildingBlocks.Domain;

namespace Hba.Delivery.Domain.ValueObjects;

/// <summary>
/// Code de remise. Il est communiqué au destinataire par SMS ou WhatsApp, et au
/// client donneur d'ordre ; le livreur ne le reçoit jamais, il le saisit.
/// C'est la preuve de livraison.
/// Immuable : une tentative échouée produit une nouvelle instance, que
/// l'agrégat substitue à la précédente.
/// </summary>
public sealed class DeliveryOtp : ValueObject
{
    public const int MaxAttempts = 5;
    private const int Digits = 6;

    private DeliveryOtp(string code, int failedAttempts)
    {
        Code = code;
        FailedAttempts = failedAttempts;
    }

    /// <summary>
    /// Code en clair. La matrice de visibilité l'autorise pour le client et le
    /// destinataire uniquement : aucune projection destinée au livreur, au
    /// commerçant, au partenaire ou à l'administrateur ne doit le contenir.
    /// </summary>
    public string Code { get; }

    public int FailedAttempts { get; }

    public bool IsLocked => FailedAttempts >= MaxAttempts;

    public static DeliveryOtp Generate()
    {
        var value = RandomNumberGenerator.GetInt32(0, 1_000_000);
        return new DeliveryOtp(value.ToString(CultureInfo.InvariantCulture).PadLeft(Digits, '0'), 0);
    }

    /// <summary>Reconstruction depuis la persistance.</summary>
    public static DeliveryOtp Restore(string code, int failedAttempts) => new(code, failedAttempts);

    /// <summary>
    /// Vérifie le code saisi par le livreur. En cas d'échec, renvoie une instance
    /// dont le compteur est incrémenté : au-delà de <see cref="MaxAttempts"/>, la
    /// remise doit passer par le support.
    /// </summary>
    public OtpVerification Verify(string? candidate)
    {
        if (IsLocked)
        {
            throw new DomainException(
                "OTP_LOCKED",
                "Trop de tentatives sur le code de remise. La livraison doit être traitée par le support.");
        }

        var ok = CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(Code),
            Encoding.UTF8.GetBytes(candidate ?? string.Empty));

        return ok
            ? new OtpVerification(true, this)
            : new OtpVerification(false, new DeliveryOtp(Code, FailedAttempts + 1));
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Code;
        yield return FailedAttempts;
    }
}

/// <summary>Résultat d'une vérification : succès, et état à conserver.</summary>
public readonly record struct OtpVerification(bool Succeeded, DeliveryOtp Otp);
