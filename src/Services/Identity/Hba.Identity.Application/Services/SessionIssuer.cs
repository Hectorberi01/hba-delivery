using Hba.Identity.Application.Ports;
using Hba.Identity.Application.Views;
using Hba.Identity.Domain.Accounts;
using Hba.Identity.Domain.Sessions;

namespace Hba.Identity.Application.Services;

/// <summary>
/// Émet un couple jeton d'accès / jeton de rafraîchissement. Utilisé par les
/// trois chemins d'authentification — OTP, mot de passe, rafraîchissement —
/// pour qu'il n'existe qu'une seule façon de créer une session.
/// </summary>
public sealed class SessionIssuer(ITokenIssuer tokens, IRefreshTokenRepository refreshTokens)
{
    public TokenPairView Issue(Account account, string? deviceId, Guid sessionId, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(account);

        var principal = AccountViewMapper.ToPrincipal(account);
        var accessToken = tokens.IssueAccessToken(principal, sessionId, now);

        var (raw, hash) = tokens.NewRefreshToken();

        var refresh = RefreshToken.Issue(
            account.Id,
            hash,
            deviceId,
            sessionId,
            now,
            tokens.RefreshTokenLifetime);

        refreshTokens.Add(refresh);

        return new TokenPairView(
            accessToken,
            raw,
            (int)tokens.AccessTokenLifetime.TotalSeconds,
            principal);
    }

    /// <summary>
    /// Rotation : le jeton présenté est consommé au profit du nouveau, dans la
    /// même session. La chaîne reste traçable d'un bout à l'autre.
    /// </summary>
    public TokenPairView Rotate(Account account, RefreshToken presented, string? deviceId, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(account);
        ArgumentNullException.ThrowIfNull(presented);

        var principal = AccountViewMapper.ToPrincipal(account);
        var accessToken = tokens.IssueAccessToken(principal, presented.SessionId, now);

        var (raw, hash) = tokens.NewRefreshToken();

        var replacement = RefreshToken.Issue(
            account.Id,
            hash,
            deviceId ?? presented.DeviceId,
            presented.SessionId,
            now,
            tokens.RefreshTokenLifetime);

        presented.Consume(replacement.Id, now);
        refreshTokens.Add(replacement);

        return new TokenPairView(
            accessToken,
            raw,
            (int)tokens.AccessTokenLifetime.TotalSeconds,
            principal);
    }
}
