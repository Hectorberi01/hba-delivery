using Hba.BuildingBlocks.Domain;

namespace Hba.Identity.Domain.Sessions;

/// <summary>
/// Jeton de rafraîchissement, à USAGE UNIQUE. S'en servir le consomme et en
/// émet un nouveau ; le présenter une seconde fois signale qu'il a été copié,
/// et toute la chaîne de la session est alors révoquée.
///
/// Seule l'empreinte est stockée : une fuite de la base ne donne pas de jeton
/// utilisable.
/// </summary>
public sealed class RefreshToken : AggregateRoot
{
    private RefreshToken()
    {
    }

    private RefreshToken(
        Guid id,
        Guid accountId,
        string tokenHash,
        string? deviceId,
        Guid sessionId,
        DateTimeOffset issuedAt,
        DateTimeOffset expiresAt) : base(id)
    {
        AccountId = accountId;
        TokenHash = tokenHash;
        DeviceId = deviceId;
        SessionId = sessionId;
        IssuedAt = issuedAt;
        ExpiresAt = expiresAt;
    }

    public Guid AccountId { get; private set; }

    /// <summary>SHA-256 du jeton présenté. Le jeton lui-même n'est jamais stocké.</summary>
    public string TokenHash { get; private set; } = string.Empty;

    public string? DeviceId { get; private set; }

    /// <summary>
    /// Chaîne de rotation : tous les jetons issus d'une même connexion la
    /// partagent. Révoquer une session révoque la chaîne entière.
    /// </summary>
    public Guid SessionId { get; private set; }

    public DateTimeOffset IssuedAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset? ConsumedAt { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public string? RevokedReason { get; private set; }

    public Guid? ReplacedByTokenId { get; private set; }

    public bool IsActive(DateTimeOffset now)
        => RevokedAt is null && ConsumedAt is null && ExpiresAt > now;

    public static RefreshToken Issue(
        Guid accountId,
        string tokenHash,
        string? deviceId,
        Guid sessionId,
        DateTimeOffset now,
        TimeSpan lifetime)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenHash);

        return new RefreshToken(
            Guid.CreateVersion7(),
            accountId,
            tokenHash,
            string.IsNullOrWhiteSpace(deviceId) ? null : deviceId,
            sessionId,
            now,
            now.Add(lifetime));
    }

    /// <summary>
    /// Consomme le jeton au profit de son successeur. Lève si le jeton est
    /// révoqué, expiré, ou déjà consommé — ce dernier cas étant celui qui doit
    /// déclencher la révocation de la session.
    /// </summary>
    public void Consume(Guid replacementId, DateTimeOffset now)
    {
        if (RevokedAt is not null)
        {
            throw new ForbiddenException("Jeton de rafraîchissement révoqué.");
        }

        if (ConsumedAt is not null)
        {
            throw new DomainException(
                "REFRESH_TOKEN_REUSED",
                "Ce jeton de rafraîchissement a déjà servi.");
        }

        if (ExpiresAt <= now)
        {
            throw new ForbiddenException("Jeton de rafraîchissement expiré.");
        }

        ConsumedAt = now;
        ReplacedByTokenId = replacementId;
    }

    public void Revoke(string reason, DateTimeOffset now)
    {
        if (RevokedAt is not null)
        {
            return;
        }

        RevokedAt = now;
        RevokedReason = reason;
    }
}
