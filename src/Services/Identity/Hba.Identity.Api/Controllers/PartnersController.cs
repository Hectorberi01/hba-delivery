using Hba.BuildingBlocks.Application.Messaging;
using Hba.BuildingBlocks.Domain;
using Hba.BuildingBlocks.Security;
using Hba.Identity.Api.Controllers.Requests;
using Hba.Identity.Application.Common.Views;
using Hba.Identity.Application.Features.Partners.Commands;
using Hba.Identity.Application.Features.Partners.Queries;
using Hba.Identity.Domain.Exceptions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Hba.Identity.Api.Controllers;

/// <summary>
/// Clients partenaires : les SYSTEMES tiers qui appellent l'API HBA, à ne pas
/// confondre avec les commerçants, qui sont des entreprises.
///
/// Comme pour les comptes, l'autorisation fine vit dans les handlers. Ici, seul
/// l'échange de jeton est anonyme, par construction : un système qui vient
/// chercher son premier jeton n'en a pas encore.
/// </summary>
[ApiController]
[Authorize]
[Produces("application/json")]
public sealed class PartnersController(IDispatcher dispatcher) : ControllerBase
{
    /// <summary>
    /// Enrôlement d'un partenaire.
    ///
    /// LA REPONSE CONTIENT LES SECRETS EN CLAIR, UNE SEULE FOIS. Ils ne sont
    /// jamais relus ensuite : s'ils sont perdus, il faut une rotation.
    /// </summary>
    [HttpPost("api/v1/partners")]
    public async Task<ActionResult<PartnerSecretsView>> Create(
        [FromBody] CreatePartnerClientBody body,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        var view = await dispatcher.SendAsync(
            new CreatePartnerClientCommand(
                body.PartnerName,
                body.Source,
                Nullify(body.WebhookUrl),
                body.Scopes,
                // Zéro n'est pas une limite, c'est une absence de valeur : le
                // JSON qui omet le champ ne doit pas fermer le partenaire.
                body.RateLimitPerMinute == 0 ? 120 : body.RateLimitPerMinute),
            cancellationToken).ConfigureAwait(false);

        return Created($"/api/v1/partners/{view.PartnerId}", view);
    }

    [HttpPost("api/v1/partners/{partnerId}/rotate-secret")]
    public async Task<ActionResult<PartnerSecretsView>> RotateSecret(
        string partnerId,
        [FromBody] ReasonBody body,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        var view = await dispatcher.SendAsync(
            new RotatePartnerSecretCommand(ParseId(partnerId), body.Reason),
            cancellationToken).ConfigureAwait(false);

        return Ok(view);
    }

    /// <summary>
    /// Configuration webhook, back-office uniquement. Elle rend le secret de
    /// signature en clair : c'est le seul endroit qui le fait, pour que le
    /// partenaire puisse le recopier.
    /// </summary>
    [HttpGet("api/v1/partners/{partnerId}/webhook-config")]
    public async Task<ActionResult<PartnerWebhookConfigView>> GetWebhookConfig(
        string partnerId,
        CancellationToken cancellationToken)
    {
        var view = await dispatcher.QueryAsync(
            new GetPartnerWebhookConfigQuery(ParseId(partnerId)),
            cancellationToken).ConfigureAwait(false);

        return Ok(view);
    }

    /// <summary>
    /// OAuth2 client credentials. Pas de jeton de rafraîchissement : un système
    /// redemande un jeton quand il en a besoin, il n'a pas de session.
    ///
    /// La route est hors du préfixe partenaires, à la même place que celle de
    /// Hba.PartnerApi : c'est l'adresse qu'un client OAuth2 s'attend à trouver.
    /// </summary>
    [HttpPost("api/v1/oauth/token")]
    [AllowAnonymous]
    [EnableRateLimiting(HbaRateLimitPolicies.PartnerToken)]
    public async Task<ActionResult<TokenPairView>> IssueToken(
        [FromBody] IssuePartnerTokenBody body,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        var pair = await dispatcher.SendAsync(
            new IssuePartnerTokenCommand(body.ClientId, body.ClientSecret, body.Scopes),
            cancellationToken).ConfigureAwait(false);

        return Ok(pair);
    }

    private static Guid ParseId(string value)
        => Guid.TryParse(value, out var id)
            ? id
            : throw new DomainException(IdentityErrorCodes.InvalidId, $"Identifiant de partenaire invalide : {value}.");

    private static string? Nullify(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
