using Hba.BuildingBlocks.Security;
using Hba.Identity.Domain.Accounts;

namespace Hba.Identity.Application.Common.Views;

/// <summary>Ce que les services liront dans le jeton.</summary>
public sealed record PrincipalView
{
    public required string SubjectId { get; init; }
    public string? Phone { get; init; }
    public string? Email { get; init; }
    public required string DisplayName { get; init; }
    public required IReadOnlyList<string> Roles { get; init; }
    public string? MerchantId { get; init; }
    public string? PartnerId { get; init; }
    public string? DriverId { get; init; }
}

public sealed record TokenPairView(string AccessToken, string RefreshToken, int ExpiresInSeconds, PrincipalView Principal)
{
    public string TokenType => "Bearer";
}

/// <summary>
/// Jeton d'un service interne. Pas de rafraichissement et pas de principal :
/// un service redemande un jeton, il n'a pas de session.
/// </summary>
public sealed record ServiceTokenView(string AccessToken, int ExpiresInSeconds)
{
    public string TokenType => "Bearer";
}

public sealed record AccountView
{
    public required Guid Id { get; init; }
    public string? Phone { get; init; }
    public string? Email { get; init; }
    public required string DisplayName { get; init; }
    public required IReadOnlyList<string> Roles { get; init; }
    public required AccountStatus Status { get; init; }
    public string? MerchantId { get; init; }
    public string? DriverId { get; init; }
    public bool HasPassword { get; init; }
    public string? StatusReason { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? LastLoginAt { get; init; }
}

/// <summary>
/// Secrets d'un client partenaire. Ce DTO ne sort qu'une fois, à la création ou
/// à la rotation : les secrets ne sont jamais relus en clair ensuite.
/// </summary>
public sealed record PartnerSecretsView(
    string PartnerId,
    string ClientId,
    string ClientSecret,
    string WebhookSigningSecret,
    IReadOnlyList<string> Scopes,
    DateTimeOffset CreatedAt);

public sealed record PartnerWebhookConfigView(
    string PartnerId,
    string? WebhookUrl,
    string SigningSecret,
    int RateLimitPerMinute,
    bool Enabled);

public static class AccountViewMapper
{
    public static AccountView ToView(Account account)
    {
        ArgumentNullException.ThrowIfNull(account);

        return new AccountView
        {
            Id = account.Id,
            Phone = account.Phone?.Value,
            Email = account.Email?.Value,
            DisplayName = account.DisplayName,
            Roles = account.Roles,
            Status = account.Status,
            MerchantId = account.MerchantId,
            // Meme derivation que pour le jeton : la console ne doit pas lire
            // un identifiant livreur vide la ou le jeton en porte un.
            DriverId = DriverIdOf(account),
            HasPassword = account.HasPassword,
            StatusReason = account.StatusReason,
            CreatedAt = account.CreatedAt,
            LastLoginAt = account.LastLoginAt,
        };
    }

    public static PrincipalView ToPrincipal(Account account)
    {
        ArgumentNullException.ThrowIfNull(account);

        return new PrincipalView
        {
            SubjectId = account.Id.ToString(),
            Phone = account.Phone?.Value,
            Email = account.Email?.Value,
            DisplayName = account.DisplayName,
            Roles = account.Roles,
            MerchantId = account.MerchantId,
            DriverId = DriverIdOf(account),
        };
    }

    /// <summary>
    /// L'IDENTIFIANT DU LIVREUR EST CELUI DU COMPTE.
    ///
    /// Le service Driver ne genere pas d'identifiant : son profil reprend
    /// l'identifiant du compte (RegisterDriverHandler), et Dispatch comme
    /// Delivery stockent cette meme valeur. Un seul identifiant traverse
    /// donc tous les services, et il n'y a aucune table de correspondance a
    /// tenir.
    ///
    /// La colonne driver_id du compte relevait d'une conception ou les deux
    /// identifiants differaient, reliee par LinkDriverProfile — que personne
    /// n'appelle. Tant qu'elle existe, elle reste prioritaire : un compte
    /// deja lie garde sa valeur. Sinon le role suffit a deduire le claim.
    ///
    /// CE QUE CE CLAIM NE DIT PAS : que le profil existe. Il nait d'un
    /// evenement Kafka, donc quelques instants apres le compte. Un appel
    /// livreur passe juste apres l'inscription peut encore rendre NOT_FOUND.
    /// </summary>
    private static string? DriverIdOf(Account account)
    {
        if (!string.IsNullOrWhiteSpace(account.DriverId))
        {
            return account.DriverId;
        }

        return account.Roles.Contains(HbaRoles.Driver, StringComparer.Ordinal)
            ? account.Id.ToString()
            : null;
    }
}
