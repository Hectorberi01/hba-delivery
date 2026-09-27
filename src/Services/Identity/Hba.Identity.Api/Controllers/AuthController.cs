using Hba.BuildingBlocks.Application.Messaging;
using Hba.BuildingBlocks.Domain;
using Hba.BuildingBlocks.Security;
using Hba.Identity.Api.Controllers.Requests;
using Hba.Identity.Application.Common.Views;
using Hba.Identity.Application.Features.Accounts.Commands;
using Hba.Identity.Application.Features.Accounts.Queries;
using Hba.Identity.Application.Features.Authentication.Commands;
using Hba.Identity.Domain.Exceptions;
using Hba.Identity.Domain.Otp;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Hba.Identity.Api.Controllers;

/// <summary>
/// Surface REST d'authentification d'Identity.
///
/// ELLE DOUBLE LE GRPC, ET IL FAUT LE SAVOIR. Les deux transports appellent le
/// MEME dispatcher et donc les MEMES handlers : aucune règle métier n'est
/// écrite deux fois, seule la traduction entrée/sortie l'est. Toute évolution
/// de commande doit néanmoins être répercutée ici ET dans IdentityGrpcService,
/// sans quoi les deux portes se mettent à répondre différemment.
///
/// L'autorisation fine reste dans les handlers, jamais ici : c'est la règle du
/// référentiel, et c'est ce qui permet aux deux surfaces d'être également
/// sûres sans se recopier l'une l'autre.
/// </summary>
[ApiController]
[Authorize]
[Route("api/v1/auth")]
[Produces("application/json")]
public sealed class AuthController(IDispatcher dispatcher) : ControllerBase
{
    // ---------------------------------------------------------------- OTP ---

    /// <summary>Demande d'un code de connexion par SMS ou WhatsApp.</summary>
    [HttpPost("otp/request")]
    [AllowAnonymous]
    [EnableRateLimiting(HbaRateLimitPolicies.OtpRequest)]
    public async Task<ActionResult<object>> RequestOtp(
        [FromBody] OtpRequestBody body,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        var result = await dispatcher.SendAsync(
            new RequestOtpCommand(body.Phone, ParseIntent(body.Intent), Nullify(body.DeviceId)),
            cancellationToken).ConfigureAwait(false);

        return Ok(new
        {
            challengeId = result.ChallengeId,
            expiresAt = result.ExpiresAt,
            retryAfterSeconds = result.RetryAfterSeconds,
        });
    }

    /// <summary>Vérification du code et ouverture de session.</summary>
    [HttpPost("otp/verify")]
    [AllowAnonymous]
    [EnableRateLimiting(HbaRateLimitPolicies.AuthAttempt)]
    public async Task<ActionResult<TokenPairView>> VerifyOtp(
        [FromBody] OtpVerifyBody body,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        var pair = await dispatcher.SendAsync(
            new VerifyOtpCommand(
                ParseId(body.ChallengeId, "défi"),
                body.Code,
                Nullify(body.DeviceId),
                Nullify(body.DisplayName)),
            cancellationToken).ConfigureAwait(false);

        return Ok(pair);
    }

    // -------------------------------------------------------- Mot de passe ---

    /// <summary>Connexion du portail commerçant et du back-office.</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting(HbaRateLimitPolicies.AuthAttempt)]
    public async Task<ActionResult<TokenPairView>> Login(
        [FromBody] LoginBody body,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        var pair = await dispatcher.SendAsync(
            new LoginWithPasswordCommand(body.Login, body.Password, Nullify(body.DeviceId)),
            cancellationToken).ConfigureAwait(false);

        return Ok(pair);
    }

    /// <summary>
    /// Changement de mot de passe. Sans AccountId, c'est le sien : un appelant
    /// ne doit pas avoir à connaître son propre identifiant pour ça.
    /// </summary>
    [HttpPost("password")]
    public async Task<IActionResult> SetPassword(
        [FromBody] ChangePasswordBody body,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        await dispatcher.SendAsync(
            new SetPasswordCommand(
                ParseId(body.AccountId ?? Subject, "compte"),
                Nullify(body.CurrentPassword),
                body.NewPassword),
            cancellationToken).ConfigureAwait(false);

        // Changer de mot de passe ferme les autres sessions : l'interface doit
        // renvoyer l'utilisateur vers l'écran de connexion.
        return NoContent();
    }

    // ------------------------------------------------------------ Sessions ---

    [HttpPost("refresh")]
    [AllowAnonymous]
    [EnableRateLimiting(HbaRateLimitPolicies.AuthAttempt)]
    public async Task<ActionResult<TokenPairView>> Refresh(
        [FromBody] RefreshBody body,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        var pair = await dispatcher.SendAsync(
            new RefreshSessionCommand(body.RefreshToken, Nullify(body.DeviceId)),
            cancellationToken).ConfigureAwait(false);

        return Ok(pair);
    }

    /// <summary>
    /// Déconnexion. Anonyme à dessein : se déconnecter ne doit pas exiger un
    /// jeton d'accès encore valide.
    /// </summary>
    [HttpPost("logout")]
    [AllowAnonymous]
    public async Task<IActionResult> Logout(
        [FromBody] RevokeBody body,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        await dispatcher.SendAsync(new RevokeSessionCommand(body.RefreshToken), cancellationToken)
            .ConfigureAwait(false);

        return NoContent();
    }

    [HttpPost("sessions/revoke-all")]
    public async Task<ActionResult<object>> RevokeAllSessions(
        [FromBody] RevokeAllBody body,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        var revoked = await dispatcher.SendAsync(
            new RevokeAllSessionsCommand(ParseId(body.AccountId ?? Subject, "compte")),
            cancellationToken).ConfigureAwait(false);

        return Ok(new { revoked });
    }

    // ------------------------------------------------------------- Profil ---

    /// <summary>Le porteur du jeton, tel que les services le liront.</summary>
    [HttpGet("me")]
    public async Task<ActionResult<PrincipalView>> Me(CancellationToken cancellationToken)
    {
        var view = await dispatcher.QueryAsync(new GetPrincipalQuery(null), cancellationToken)
            .ConfigureAwait(false);

        return Ok(view);
    }

    // ------------------------------------------------------------- Outils ---

    private string Subject => User.FindFirst("sub")?.Value ?? string.Empty;

    /// <summary>
    /// L'intention décide du rôle donné au compte créé à la première connexion.
    /// Absente, c'est un client : c'est le cas très majoritaire, et un livreur
    /// passe par une application qui, elle, le précise.
    /// </summary>
    private static OtpIntent ParseIntent(string? intent) => intent?.Trim().ToLowerInvariant() switch
    {
        "driver" => OtpIntent.Driver,
        _ => OtpIntent.Customer,
    };

    private static Guid ParseId(string value, string label)
        => Guid.TryParse(value, out var id)
            ? id
            : throw new DomainException(IdentityErrorCodes.InvalidId, $"Identifiant de {label} invalide : {value}.");

    private static string? Nullify(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
