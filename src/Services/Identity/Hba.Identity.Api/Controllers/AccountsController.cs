using Hba.BuildingBlocks.Application.Messaging;
using Hba.BuildingBlocks.Domain;
using Hba.Identity.Api.Controllers.Requests;
using Hba.Identity.Application.Common.Views;
using Hba.Identity.Application.Features.Accounts.Commands;
using Hba.Identity.Domain.Exceptions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hba.Identity.Api.Controllers;

/// <summary>
/// Gestion des comptes, réservée au back-office.
///
/// AUCUNE POLITIQUE D'AUTORISATION N'EST POSEE SUR CES ACTIONS, ET C'EST VOULU.
/// Chaque handler vérifie déjà le rôle de l'appelant (AccountGuard.EnsureAdmin
/// ou EnsureBackOffice). Recopier la règle en attribut donnerait deux endroits
/// à modifier le jour où elle change, et le jour où l'un des deux est oublié,
/// c'est l'attribut qui donne la fausse impression de sûreté. [Authorize] seul
/// exige un jeton valide ; le reste se décide là où la règle est écrite.
/// </summary>
[ApiController]
[Authorize]
[Route("api/v1/accounts")]
[Produces("application/json")]
public sealed class AccountsController(IDispatcher dispatcher) : ControllerBase
{
    /// <summary>Création d'un compte admin, ops, support ou finance.</summary>
    [HttpPost("back-office")]
    public async Task<ActionResult<AccountView>> CreateBackOffice(
        [FromBody] CreateBackOfficeAccountBody body,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        var view = await dispatcher.SendAsync(
            new CreateBackOfficeAccountCommand(body.Email, body.DisplayName, body.Roles, body.InitialPassword),
            cancellationToken).ConfigureAwait(false);

        return Created($"/api/v1/accounts/{view.Id}", view);
    }

    /// <summary>
    /// Création d'un compte rattaché à un commerçant.
    ///
    /// COMMERCANT EST UNE ENTREPRISE, PAS UNE PERSONNE : ce compte est celui
    /// d'un merchant_owner ou d'un merchant_staff qui travaille pour elle.
    /// </summary>
    [HttpPost("merchant")]
    public async Task<ActionResult<AccountView>> CreateMerchant(
        [FromBody] CreateMerchantAccountBody body,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        var view = await dispatcher.SendAsync(
            new CreateMerchantAccountCommand(
                body.MerchantId,
                Nullify(body.Email),
                Nullify(body.Phone),
                body.DisplayName,
                body.Role,
                body.InitialPassword),
            cancellationToken).ConfigureAwait(false);

        return Created($"/api/v1/accounts/{view.Id}", view);
    }

    [HttpPut("{accountId}/roles")]
    public async Task<ActionResult<AccountView>> SetRoles(
        string accountId,
        [FromBody] SetAccountRolesBody body,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        var view = await dispatcher.SendAsync(
            new SetAccountRolesCommand(ParseId(accountId), body.Roles, body.Reason),
            cancellationToken).ConfigureAwait(false);

        return Ok(view);
    }

    /// <summary>
    /// Suspension. La raison est obligatoire : un compte fermé sans motif est
    /// un compte que personne ne saura rouvrir en confiance.
    /// </summary>
    [HttpPost("{accountId}/suspend")]
    public async Task<ActionResult<AccountView>> Suspend(
        string accountId,
        [FromBody] ReasonBody body,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        var view = await dispatcher.SendAsync(
            new SuspendAccountCommand(ParseId(accountId), body.Reason),
            cancellationToken).ConfigureAwait(false);

        return Ok(view);
    }

    [HttpPost("{accountId}/reactivate")]
    public async Task<ActionResult<AccountView>> Reactivate(
        string accountId,
        [FromBody] ReasonBody body,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        var view = await dispatcher.SendAsync(
            new ReactivateAccountCommand(ParseId(accountId), body.Reason),
            cancellationToken).ConfigureAwait(false);

        return Ok(view);
    }

    /// <summary>
    /// Rattachement d'un compte à une fiche livreur.
    ///
    /// CETTE ROUTE N'A PAS D'EQUIVALENT GRPC : la commande existe dans
    /// l'application mais IdentityGrpcService ne l'expose pas. C'est le premier
    /// point où les deux surfaces divergent — à trancher : soit on l'ajoute au
    /// contrat proto, soit on l'enlève ici.
    /// </summary>
    [HttpPost("{accountId}/driver-profile")]
    public async Task<ActionResult<AccountView>> LinkDriverProfile(
        string accountId,
        [FromBody] LinkDriverProfileBody body,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        var view = await dispatcher.SendAsync(
            new LinkDriverProfileCommand(ParseId(accountId), body.DriverId),
            cancellationToken).ConfigureAwait(false);

        return Ok(view);
    }

    private static Guid ParseId(string value)
        => Guid.TryParse(value, out var id)
            ? id
            : throw new DomainException(IdentityErrorCodes.InvalidId, $"Identifiant de compte invalide : {value}.");

    private static string? Nullify(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
