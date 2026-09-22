using Hba.BuildingBlocks.Application.Abstractions;
using Hba.BuildingBlocks.Application.Messaging;
using Hba.BuildingBlocks.Domain;
using Hba.BuildingBlocks.Security;
using Hba.Identity.Application.Ports;
using Hba.Identity.Application.Views;
using Hba.Identity.Domain.Partners;
using Hba.Identity.Domain.ValueObjects;

namespace Hba.Identity.Application.Partners;

public sealed class CreatePartnerClientHandler(
    IPartnerClientRepository partners,
    IClientCredentialsFactory credentials,
    ISecretProtector protector,
    ICallerContext caller,
    IUnitOfWork unitOfWork,
    IClock clock) : ICommandHandler<CreatePartnerClientCommand, PartnerSecretsView>
{
    public async Task<PartnerSecretsView> HandleAsync(
        CreatePartnerClientCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!caller.IsInRole(HbaRoles.Admin))
        {
            throw new ForbiddenException("Seul un administrateur crée un client partenaire.");
        }

        var clientId = credentials.NewClientId();
        var clientSecret = credentials.NewSecret();
        var webhookSecret = credentials.NewSecret();

        var partner = PartnerClient.Create(
            Guid.CreateVersion7(),
            command.PartnerName,
            command.Source,
            clientId,
            PasswordHash.FromPlainText(clientSecret),
            command.WebhookUrl,
            protector.Protect(webhookSecret),
            command.Scopes ?? [],
            command.RateLimitPerMinute,
            caller.ToActor(),
            clock.UtcNow);

        partners.Add(partner);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // Seule et unique occasion de voir ces deux secrets en clair.
        return new PartnerSecretsView(
            partner.Id.ToString(),
            partner.ClientId,
            clientSecret,
            webhookSecret,
            partner.Scopes,
            partner.CreatedAt);
    }
}

public sealed class RotatePartnerSecretHandler(
    IPartnerClientRepository partners,
    IClientCredentialsFactory credentials,
    ISecretProtector protector,
    ICallerContext caller,
    IUnitOfWork unitOfWork,
    IClock clock) : ICommandHandler<RotatePartnerSecretCommand, PartnerSecretsView>
{
    public async Task<PartnerSecretsView> HandleAsync(
        RotatePartnerSecretCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!caller.IsInRole(HbaRoles.Admin))
        {
            throw new ForbiddenException("Seul un administrateur fait tourner un secret partenaire.");
        }

        var partner = await partners.GetByIdAsync(command.PartnerId, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException("Partenaire", command.PartnerId.ToString());

        var clientSecret = credentials.NewSecret();
        var webhookSecret = credentials.NewSecret();

        partner.RotateSecret(
            PasswordHash.FromPlainText(clientSecret),
            protector.Protect(webhookSecret),
            command.Reason,
            caller.ToActor(),
            clock.UtcNow);

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // Les jetons déjà émis restent valides jusqu'à expiration : c'est pour
        // cela qu'ils sont courts. Rien à révoquer, un partenaire n'a pas de
        // session.

        return new PartnerSecretsView(
            partner.Id.ToString(),
            partner.ClientId,
            clientSecret,
            webhookSecret,
            partner.Scopes,
            clock.UtcNow);
    }
}

public sealed class IssuePartnerTokenHandler(
    IPartnerClientRepository partners,
    ITokenIssuer tokens,
    IClock clock) : ICommandHandler<IssuePartnerTokenCommand, TokenPairView>
{
    public async Task<TokenPairView> HandleAsync(
        IssuePartnerTokenCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var partner = await partners
            .GetByClientIdAsync(command.ClientId ?? string.Empty, cancellationToken)
            .ConfigureAwait(false);

        // Client inconnu et secret faux donnent la même réponse.
        if (partner is null || !partner.VerifySecret(command.ClientSecret))
        {
            throw new ForbiddenException("Identifiants client invalides.");
        }

        partner.EnsureUsable();

        // Lève si le partenaire demande une portée qu'on ne lui a pas accordée.
        partner.ResolveScopes(command.Scopes);

        var principal = new PrincipalView
        {
            SubjectId = partner.Id.ToString(),
            DisplayName = partner.Name,
            Roles = [HbaRoles.Partner],
            PartnerId = partner.Id.ToString(),
        };

        var now = clock.UtcNow;
        var accessToken = tokens.IssueAccessToken(principal, Guid.CreateVersion7(), now);

        // Chaîne vide et non un jeton factice : un partenaire ne rafraîchit pas,
        // il redemande.
        return new TokenPairView(
            accessToken,
            string.Empty,
            (int)tokens.AccessTokenLifetime.TotalSeconds,
            principal);
    }
}

public sealed class GetPartnerWebhookConfigHandler(
    IPartnerClientRepository partners,
    ISecretProtector protector,
    ICallerContext caller) : IQueryHandler<GetPartnerWebhookConfigQuery, PartnerWebhookConfigView>
{
    public async Task<PartnerWebhookConfigView> HandleAsync(
        GetPartnerWebhookConfigQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (!caller.Roles.Overlaps(HbaRoles.BackOffice))
        {
            throw new ForbiddenException("Réservé au back-office.");
        }

        var partner = await partners.GetByIdAsync(query.PartnerId, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException("Partenaire", query.PartnerId.ToString());

        return new PartnerWebhookConfigView(
            partner.Id.ToString(),
            partner.WebhookUrl,
            protector.Unprotect(partner.ProtectedWebhookSecret),
            partner.RateLimitPerMinute,
            partner.Enabled);
    }
}
