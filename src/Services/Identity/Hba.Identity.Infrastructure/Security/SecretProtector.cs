using System.Security.Cryptography;
using System.Text;
using Hba.Identity.Application.Ports;
using Microsoft.Extensions.Options;

namespace Hba.Identity.Infrastructure.Security;

public sealed class DataProtectionOptions
{
    public const string SectionName = "DataProtection";

    /// <summary>Clé AES de 32 octets, en base64.</summary>
    public string? KeyBase64 { get; set; }

    /// <summary>Autorise une clé fixe de développement. Jamais en production.</summary>
    public bool AllowDevelopmentKey { get; set; }
}

/// <summary>
/// AES-256-GCM. Sert au seul secret qui doit pouvoir être relu : la clé de
/// signature des webhooks partenaires. Tout le reste est haché.
///
/// Format : base64( nonce(12) || tag(16) || chiffré ).
/// </summary>
public sealed class AesGcmSecretProtector : ISecretProtector
{
    private const int NonceSize = 12;
    private const int TagSize = 16;

    // La clé est conservée, pas l'instance AesGcm : celle-ci n'est pas
    // documentée comme utilisable depuis plusieurs fils d'exécution, et ce
    // service est un singleton.
    private readonly byte[] _key;

    public AesGcmSecretProtector(IOptions<DataProtectionOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var value = options.Value;

        byte[] key;

        if (!string.IsNullOrWhiteSpace(value.KeyBase64))
        {
            key = Convert.FromBase64String(value.KeyBase64);

            if (key.Length != 32)
            {
                throw new InvalidOperationException(
                    "DataProtection:KeyBase64 doit contenir exactement 32 octets (AES-256).");
            }
        }
        else if (value.AllowDevelopmentKey)
        {
            // Déterministe pour que les secrets écrits hier se relisent
            // aujourd'hui sur le même poste. Inutilisable ailleurs : la clé est
            // dans le code source.
            key = SHA256.HashData(Encoding.UTF8.GetBytes("hba-delivery-development-only"));
        }
        else
        {
            throw new InvalidOperationException(
                "Aucune clé de chiffrement configurée. Renseignez DataProtection:KeyBase64.");
        }

        _key = key;
    }

    public string Protect(string plainText)
    {
        ArgumentNullException.ThrowIfNull(plainText);

        var plain = Encoding.UTF8.GetBytes(plainText);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var cipher = new byte[plain.Length];
        var tag = new byte[TagSize];

        using var aes = new AesGcm(_key, TagSize);
        aes.Encrypt(nonce, plain, cipher, tag);

        var output = new byte[NonceSize + TagSize + cipher.Length];
        nonce.CopyTo(output, 0);
        tag.CopyTo(output, NonceSize);
        cipher.CopyTo(output, NonceSize + TagSize);

        return Convert.ToBase64String(output);
    }

    public string Unprotect(string protectedText)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(protectedText);

        var input = Convert.FromBase64String(protectedText);

        if (input.Length < NonceSize + TagSize)
        {
            throw new CryptographicException("Secret chiffré illisible.");
        }

        var nonce = input.AsSpan(0, NonceSize);
        var tag = input.AsSpan(NonceSize, TagSize);
        var cipher = input.AsSpan(NonceSize + TagSize);
        var plain = new byte[cipher.Length];

        using var aes = new AesGcm(_key, TagSize);
        aes.Decrypt(nonce, cipher, tag, plain);

        return Encoding.UTF8.GetString(plain);
    }
}

/// <summary>Identifiants OAuth2 : aléatoires, jamais dérivés du nom du partenaire.</summary>
public sealed class ClientCredentialsFactory : IClientCredentialsFactory
{
    public string NewClientId() => "hba_" + Convert.ToHexString(RandomNumberGenerator.GetBytes(12)).ToLowerInvariant();

    public string NewSecret() => Microsoft.IdentityModel.Tokens.Base64UrlEncoder.Encode(
        RandomNumberGenerator.GetBytes(32));
}
