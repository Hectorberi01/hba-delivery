using Hba.BuildingBlocks.Application.Messaging;
using Hba.Identity.Application.Views;

namespace Hba.Identity.Application.Partners;

public sealed record CreatePartnerClientCommand(
    string PartnerName,
    string Source,
    string? WebhookUrl,
    IReadOnlyList<string> Scopes,
    int RateLimitPerMinute) : ICommand<PartnerSecretsView>;

public sealed record RotatePartnerSecretCommand(Guid PartnerId, string Reason) : ICommand<PartnerSecretsView>;

/// <summary>
/// OAuth2 client credentials. Pas de jeton de rafraîchissement : un système
/// redemande un jeton quand il en a besoin, il n'a pas de session.
/// </summary>
public sealed record IssuePartnerTokenCommand(
    string ClientId,
    string ClientSecret,
    IReadOnlyList<string>? Scopes) : ICommand<TokenPairView>;

public sealed record GetPartnerWebhookConfigQuery(Guid PartnerId) : IQuery<PartnerWebhookConfigView>;
