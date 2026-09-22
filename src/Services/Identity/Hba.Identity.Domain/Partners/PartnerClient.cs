using Hba.BuildingBlocks.Domain;
using Hba.Identity.Domain.Partners.Events;
using Hba.Identity.Domain.ValueObjects;

namespace Hba.Identity.Domain.Partners;

/// <summary>
/// Client OAuth2 d'un partenaire B2B. Un partenaire est un SYSTÈME, pas une
/// personne : il n'a ni téléphone, ni OTP, ni mot de passe au sens d'un compte.
/// Il présente un client_id et un client_secret, et reçoit un jeton portant le
/// rôle partner et son partner_id.
/// </summary>
public sealed class PartnerClient : AggregateRoot
{
    private readonly List<string> _scopes = [];

    private PartnerClient()
    {
    }

    private PartnerClient(
        Guid id,
        string name,
        string source,
        string clientId,
        PasswordHash secretHash,
        string? webhookUrl,
        string protectedWebhookSecret,
        IEnumerable<string> scopes,
        int rateLimitPerMinute,
        DateTimeOffset createdAt) : base(id)
    {
        Name = name;
        Source = source;
        ClientId = clientId;
        SecretHash = secretHash;
        WebhookUrl = webhookUrl;
        ProtectedWebhookSecret = protectedWebhookSecret;
        RateLimitPerMinute = rateLimitPerMinute;
        CreatedAt = createdAt;
        Enabled = true;
        _scopes.AddRange(scopes);
    }

    public string Name { get; private set; } = string.Empty;

    /// <summary>HBA_EXPRESS, HBA_FOOD ou PARTNER_API.</summary>
    public string Source { get; private set; } = string.Empty;

    public string ClientId { get; private set; } = string.Empty;

    /// <summary>
    /// Empreinte du secret client, avec le même algorithme que les mots de
    /// passe. Le secret n'est lisible qu'à l'émission.
    /// </summary>
    public PasswordHash SecretHash { get; private set; } = null!;

    public string? WebhookUrl { get; private set; }

    /// <summary>
    /// Secret de signature des webhooks, CHIFFRÉ. Il n'est pas haché : la
    /// Partner API doit pouvoir le relire pour signer ses appels sortants. Le
    /// chiffrement est fait par l'infrastructure ; le domaine ne voit qu'une
    /// chaîne opaque.
    /// </summary>
    public string ProtectedWebhookSecret { get; private set; } = string.Empty;

    public IReadOnlyList<string> Scopes => _scopes.AsReadOnly();

    /// <summary>Forme persistée des portées. Lue et écrite par EF Core seulement.</summary>
    private string ScopesRaw
    {
        get => string.Join(',', _scopes);
        set
        {
            _scopes.Clear();

            if (!string.IsNullOrWhiteSpace(value))
            {
                _scopes.AddRange(value.Split(',', StringSplitOptions.RemoveEmptyEntries));
            }
        }
    }

    public int RateLimitPerMinute { get; private set; }

    public bool Enabled { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? SecretRotatedAt { get; private set; }

    public static readonly IReadOnlySet<string> KnownSources = new HashSet<string>(StringComparer.Ordinal)
    {
        "HBA_EXPRESS", "HBA_FOOD", "PARTNER_API",
    };

    public static PartnerClient Create(
        Guid id,
        string name,
        string source,
        string clientId,
        PasswordHash secretHash,
        string? webhookUrl,
        string protectedWebhookSecret,
        IEnumerable<string> scopes,
        int rateLimitPerMinute,
        Actor actor,
        DateTimeOffset createdAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);
        ArgumentNullException.ThrowIfNull(secretHash);

        if (!KnownSources.Contains(source))
        {
            throw new DomainException(
                "UNKNOWN_SOURCE",
                $"Source inconnue : {source}. Attendu : HBA_EXPRESS, HBA_FOOD ou PARTNER_API.");
        }

        if (!string.IsNullOrWhiteSpace(webhookUrl))
        {
            if (!Uri.TryCreate(webhookUrl, UriKind.Absolute, out var endpoint))
            {
                throw new DomainException("INVALID_WEBHOOK_URL", $"URL de webhook invalide : {webhookUrl}.");
            }

            if (!string.Equals(endpoint.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal))
            {
                throw new DomainException(
                    "INSECURE_WEBHOOK_URL",
                    "Un webhook partenaire doit être en HTTPS : la signature authentifie l'appel, elle n'en protège pas le contenu.");
            }
        }

        if (rateLimitPerMinute <= 0)
        {
            throw new DomainException("INVALID_RATE_LIMIT", "Le quota par minute doit être strictement positif.");
        }

        var partner = new PartnerClient(
            id, name.Trim(), source, clientId, secretHash, webhookUrl,
            protectedWebhookSecret, scopes ?? [], rateLimitPerMinute, createdAt);

        partner.Raise(new PartnerClientRegistered(id, partner.Name, source, actor, createdAt));

        return partner;
    }

    public void RotateSecret(
        PasswordHash newSecretHash,
        string newProtectedWebhookSecret,
        string reason,
        Actor actor,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(newSecretHash);
        ArgumentNullException.ThrowIfNull(actor);

        SecretHash = newSecretHash;
        ProtectedWebhookSecret = newProtectedWebhookSecret;
        SecretRotatedAt = now;

        Raise(new PartnerSecretRotated(Id, reason, actor, now));
    }

    public void Disable() => Enabled = false;

    public void EnsureUsable()
    {
        if (!Enabled)
        {
            throw new ForbiddenException("Ce client partenaire est désactivé.");
        }
    }

    /// <summary>
    /// Vérifie le secret présenté. Les portées demandées doivent être incluses
    /// dans celles accordées : un partenaire ne s'attribue pas de droits.
    /// </summary>
    public bool VerifySecret(string? candidate) => SecretHash.Verify(candidate);

    public IReadOnlyList<string> ResolveScopes(IEnumerable<string>? requested)
    {
        if (requested is null)
        {
            return Scopes;
        }

        var asked = requested.Where(s => !string.IsNullOrWhiteSpace(s)).Distinct(StringComparer.Ordinal).ToList();

        if (asked.Count == 0)
        {
            return Scopes;
        }

        var refused = asked.Where(s => !_scopes.Contains(s, StringComparer.Ordinal)).ToList();

        if (refused.Count > 0)
        {
            throw new ForbiddenException($"Portées non accordées : {string.Join(", ", refused)}.");
        }

        return asked;
    }
}
