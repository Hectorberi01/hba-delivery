using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Hba.Identity.Application.Ports;
using Microsoft.Extensions.Options;

namespace Hba.Identity.Infrastructure.Otp;

public sealed class OtpOptions
{
    public const string SectionName = "Otp";

    /// <summary>
    /// Poivre du hachage des codes. Sans lui, six chiffres se retrouvent par
    /// force brute en une fraction de seconde à partir de l'empreinte.
    /// </summary>
    public string? Pepper { get; set; }

    public bool AllowDevelopmentPepper { get; set; }

    /// <summary>Délai minimal entre deux envois pour un même numéro.</summary>
    public int ResendCooldownSeconds { get; set; } = 60;

    /// <summary>
    /// Nombre maximal de codes envoyés à un même numéro en une heure. Au-delà,
    /// c'est du harcèlement pour le titulaire et de la dépense pure pour HBA.
    /// </summary>
    public int MaxPerHour { get; set; } = 5;

    /// <summary>
    /// En développement, force un code fixe pour éviter d'avoir à lire les
    /// journaux à chaque connexion. Inutilisable en production.
    /// </summary>
    public string? FixedCodeForDevelopment { get; set; }
}

public sealed class OtpCodeService : IOtpCodeService, IDisposable
{
    private readonly HMACSHA256 _hmac;
    private readonly string? _fixedCode;

    public OtpCodeService(IOptions<OtpOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var value = options.Value;

        var pepper = value.Pepper;

        if (string.IsNullOrWhiteSpace(pepper))
        {
            if (!value.AllowDevelopmentPepper)
            {
                throw new InvalidOperationException(
                    "Aucun poivre configuré pour les codes OTP. Renseignez Otp:Pepper.");
            }

            pepper = "hba-delivery-development-only";
        }

        _hmac = new HMACSHA256(Encoding.UTF8.GetBytes(pepper));
        _fixedCode = value.FixedCodeForDevelopment;
    }

    public string GenerateCode()
    {
        if (!string.IsNullOrWhiteSpace(_fixedCode))
        {
            return _fixedCode;
        }

        // Tirage uniforme sur 000000–999999 : pas de modulo biaisé.
        var value = RandomNumberGenerator.GetInt32(0, 1_000_000);
        return value.ToString(CultureInfo.InvariantCulture).PadLeft(6, '0');
    }

    public string Hash(Guid challengeId, string code)
    {
        var payload = Encoding.UTF8.GetBytes($"{challengeId:N}:{code}");

        lock (_hmac)
        {
            return Convert.ToBase64String(_hmac.ComputeHash(payload));
        }
    }

    public void Dispose() => _hmac.Dispose();
}
