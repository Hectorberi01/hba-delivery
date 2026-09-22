using Hba.Identity.Application.Views;
using Hba.Identity.Domain.Otp;

namespace Hba.Identity.Application.Ports;

/// <summary>
/// Stockage des défis OTP. Redis : cinq minutes de durée de vie, aucune valeur
/// à conserver après.
/// </summary>
public interface IOtpStore
{
    Task SaveAsync(OtpChallenge challenge, CancellationToken cancellationToken);

    Task<OtpChallenge?> GetAsync(Guid challengeId, CancellationToken cancellationToken);

    Task DeleteAsync(Guid challengeId, CancellationToken cancellationToken);
}

/// <summary>
/// Limitation de débit des envois de SMS. Un SMS coûte de l'argent, et un
/// numéro qu'on bombarde est un numéro qu'on harcèle.
/// </summary>
public interface IOtpRateLimiter
{
    /// <summary>
    /// Renvoie null si l'envoi est autorisé, sinon le délai avant de pouvoir
    /// redemander un code.
    /// </summary>
    Task<TimeSpan?> TryAcquireAsync(string phone, CancellationToken cancellationToken);
}

/// <summary>Génération et hachage des codes à usage unique.</summary>
public interface IOtpCodeService
{
    string GenerateCode();

    /// <summary>
    /// Empreinte liée au défi : le même code pour deux défis donne deux
    /// empreintes différentes.
    /// </summary>
    string Hash(Guid challengeId, string code);
}

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

/// <summary>
/// Chiffrement réversible, pour les seules données qui doivent être relues :
/// aujourd'hui, le secret de signature des webhooks partenaires.
/// </summary>
public interface ISecretProtector
{
    string Protect(string plainText);

    string Unprotect(string protectedText);
}

/// <summary>Génération des identifiants et secrets OAuth2.</summary>
public interface IClientCredentialsFactory
{
    string NewClientId();

    string NewSecret();
}

/// <summary>
/// Envoi du code par SMS. Identity ne parle à aucun opérateur : il dépose une
/// commande dans son Outbox, à destination du service Notification.
/// </summary>
public interface IOtpNotifier
{
    void SendOtp(string phone, string code, TimeSpan validity);
}
