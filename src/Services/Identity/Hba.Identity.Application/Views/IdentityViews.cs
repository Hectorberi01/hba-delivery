using Hba.Identity.Domain.Accounts;

namespace Hba.Identity.Application.Views;

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

public sealed record TokenPairView(
    string AccessToken,
    string RefreshToken,
    int ExpiresInSeconds,
    PrincipalView Principal)
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
            DriverId = account.DriverId,
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
            DriverId = account.DriverId,
        };
    }
}
