using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Hba.Identity.Infrastructure.Security;

public sealed class JwtSigningOptions
{
    public const string SectionName = "JwtSigning";

    /// <summary>Clé privée RSA au format PEM. Prioritaire sur le chemin de fichier.</summary>
    public string? PrivateKeyPem { get; set; }

    /// <summary>Chemin d'un fichier PEM sur disque.</summary>
    public string? PrivateKeyPath { get; set; }

    /// <summary>
    /// Anciennes clés publiques, au format PEM. Publiées dans les JWKS pour que
    /// les jetons émis avant une rotation restent vérifiables.
    /// </summary>
    public IList<string> RetiredPublicKeysPem { get; } = [];

    /// <summary>
    /// Autorise la génération d'une clé au démarrage quand il n'y en a pas.
    /// À n'activer qu'en développement.
    /// </summary>
    public bool AllowEphemeralKey { get; set; }
}

/// <summary>
/// Fournit la clé de signature des jetons et l'ensemble des clés publiées dans
/// les JWKS. Une seule clé signe à la fois ; les anciennes restent publiées le
/// temps que les jetons émis avec elles expirent.
/// </summary>
public interface ISigningKeyProvider
{
    SigningCredentials ActiveCredentials { get; }

    IReadOnlyList<JsonWebKey> PublicKeys { get; }
}

public sealed class RsaSigningKeyProvider : ISigningKeyProvider, IDisposable
{
    private readonly List<RSA> _rsaInstances = [];

    public RsaSigningKeyProvider(IOptions<JwtSigningOptions> options, ILogger<RsaSigningKeyProvider> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        var value = options.Value;

        var privateKey = LoadPrivateKey(value, logger);
        _rsaInstances.Add(privateKey);

        var active = new RsaSecurityKey(privateKey) { KeyId = ComputeKeyId(privateKey) };
        ActiveCredentials = new SigningCredentials(active, SecurityAlgorithms.RsaSha256);

        var keys = new List<JsonWebKey> { ToPublicJwk(active) };

        foreach (var pem in value.RetiredPublicKeysPem)
        {
            var retired = RSA.Create();
            retired.ImportFromPem(pem);
            _rsaInstances.Add(retired);

            keys.Add(ToPublicJwk(new RsaSecurityKey(retired) { KeyId = ComputeKeyId(retired) }));
        }

        PublicKeys = keys;
    }

    public SigningCredentials ActiveCredentials { get; }

    public IReadOnlyList<JsonWebKey> PublicKeys { get; }

    private static RSA LoadPrivateKey(JwtSigningOptions options, ILogger logger)
    {
        if (!string.IsNullOrWhiteSpace(options.PrivateKeyPem))
        {
            var rsa = RSA.Create();
            rsa.ImportFromPem(options.PrivateKeyPem);
            return rsa;
        }

        if (!string.IsNullOrWhiteSpace(options.PrivateKeyPath) && File.Exists(options.PrivateKeyPath))
        {
            var rsa = RSA.Create();
            rsa.ImportFromPem(File.ReadAllText(options.PrivateKeyPath));
            return rsa;
        }

        if (!options.AllowEphemeralKey)
        {
            // Une clé générée au démarrage invaliderait tous les jetons à chaque
            // redémarrage, et différerait d'une instance à l'autre.
            throw new InvalidOperationException(
                "Aucune clé de signature configurée. Renseignez JwtSigning:PrivateKeyPem ou JwtSigning:PrivateKeyPath.");
        }

        var generated = RSA.Create(2048);

        if (!string.IsNullOrWhiteSpace(options.PrivateKeyPath))
        {
            var directory = Path.GetDirectoryName(options.PrivateKeyPath);

            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(options.PrivateKeyPath, generated.ExportPkcs8PrivateKeyPem());
            logger.LogWarning(
                "Clé de signature générée et écrite dans {Path}. Acceptable en développement uniquement.",
                options.PrivateKeyPath);
        }
        else
        {
            logger.LogWarning(
                "Clé de signature générée en mémoire : les jetons seront invalidés au prochain redémarrage.");
        }

        return generated;
    }

    /// <summary>
    /// Identifiant dérivé de la clé publique : deux instances du service qui
    /// partagent la même clé annoncent le même kid.
    /// </summary>
    private static string ComputeKeyId(RSA rsa)
    {
        var publicKey = rsa.ExportSubjectPublicKeyInfo();
        var digest = SHA256.HashData(publicKey);

        return Base64UrlEncoder.Encode(digest[..16]);
    }

    private static JsonWebKey ToPublicJwk(RsaSecurityKey key)
    {
        // Conversion à partir des seuls paramètres publics : la clé privée ne
        // doit jamais se retrouver dans le document JWKS.
        var parameters = key.Rsa!.ExportParameters(includePrivateParameters: false);

        var jwk = JsonWebKeyConverter.ConvertFromRSASecurityKey(new RsaSecurityKey(parameters));
        jwk.Kid = key.KeyId;
        jwk.Use = "sig";
        jwk.Alg = SecurityAlgorithms.RsaSha256;

        return jwk;
    }

    public void Dispose()
    {
        foreach (var rsa in _rsaInstances)
        {
            rsa.Dispose();
        }
    }
}
