using System.Security.Cryptography;
using Hba.Identity.Application.Ports;
using Hba.Identity.Application.Views;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Hba.Identity.Infrastructure.Security;

public sealed class TokenOptions
{
    public const string SectionName = "Tokens";

    public string Issuer { get; set; } = "https://identity.hba.delivery";

    public string Audience { get; set; } = "hba-delivery";

    /// <summary>
    /// Court par construction : c'est ce qui rend une suspension effective
    /// rapidement, puisqu'un jeton d'accès n'est pas révocable.
    /// </summary>
    public int AccessTokenMinutes { get; set; } = 15;

    /// <summary>
    /// Long, parce qu'un livreur ne doit pas se reconnecter tous les matins.
    /// Compensé par la rotation à usage unique et la détection de rejeu.
    /// </summary>
    public int RefreshTokenDays { get; set; } = 30;
}

public sealed class JwtTokenIssuer(ISigningKeyProvider keys, IOptions<TokenOptions> options) : ITokenIssuer
{
    private readonly TokenOptions _options = options.Value;
    private static readonly JsonWebTokenHandler Handler = new();

    public TimeSpan AccessTokenLifetime => TimeSpan.FromMinutes(_options.AccessTokenMinutes);

    public TimeSpan RefreshTokenLifetime => TimeSpan.FromDays(_options.RefreshTokenDays);

    public string IssueAccessToken(PrincipalView principal, Guid sessionId, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(principal);

        var claims = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["sub"] = principal.SubjectId,
            ["name"] = principal.DisplayName,
            // Tableau JSON : le middleware en fait autant de claims « roles ».
            ["roles"] = principal.Roles.ToArray(),
            // Identifiant de session, pour rapprocher un jeton d'accès de sa
            // chaîne de rafraîchissement dans les journaux.
            ["sid"] = sessionId.ToString(),
            ["jti"] = Guid.CreateVersion7().ToString(),
        };

        AddIfPresent(claims, "merchant_id", principal.MerchantId);
        AddIfPresent(claims, "partner_id", principal.PartnerId);
        AddIfPresent(claims, "driver_id", principal.DriverId);
        AddIfPresent(claims, "phone", principal.Phone);
        AddIfPresent(claims, "email", principal.Email);

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = _options.Issuer,
            Audience = _options.Audience,
            Claims = claims,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = now.Add(AccessTokenLifetime).UtcDateTime,
            SigningCredentials = keys.ActiveCredentials,
        };

        return Handler.CreateToken(descriptor);
    }

    /// <summary>
    /// 32 octets aléatoires. Le client reçoit la valeur, la base ne garde que
    /// son empreinte : une fuite de la table ne donne aucun jeton utilisable.
    /// </summary>
    public (string Token, string Hash) NewRefreshToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        var token = Base64UrlEncoder.Encode(bytes);

        return (token, HashRefreshToken(token));
    }

    public string HashRefreshToken(string token)
    {
        var digest = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token ?? string.Empty));
        return Convert.ToBase64String(digest);
    }

    private static void AddIfPresent(IDictionary<string, object> claims, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            claims[name] = value;
        }
    }
}
