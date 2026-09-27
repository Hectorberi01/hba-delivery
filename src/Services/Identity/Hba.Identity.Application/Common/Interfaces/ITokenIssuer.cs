using Hba.Identity.Application.Common.Views;

namespace Hba.Identity.Application.Common.Interfaces;

/// <summary>Émission des jetons d'accès signés.</summary>
public interface ITokenIssuer
{
    TimeSpan AccessTokenLifetime { get; }

    TimeSpan RefreshTokenLifetime { get; }

    string IssueAccessToken(PrincipalView principal, Guid sessionId, DateTimeOffset now);

    /// <summary>
    /// Produit un jeton de rafraîchissement : la valeur remise au client et
    /// l'empreinte conservée en base.
    /// </summary>
    (string Token, string Hash) NewRefreshToken();

    string HashRefreshToken(string token);
}
