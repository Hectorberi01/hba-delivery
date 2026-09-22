using Hba.BuildingBlocks.Security;
using Hba.Contracts.Identity.V1;

namespace Hba.Bff.Web.Endpoints;

/// <summary>
/// Le portail commerçant et le back-office se connectent par mot de passe, pas
/// par SMS : ce sont des postes de travail, souvent partagés, et un code à six
/// chiffres n'y apporterait rien.
/// </summary>
public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapWebAuthEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var anonymous = app.MapGroup("/api/web/v1/auth")
            .AllowAnonymous()
            .RequireRateLimiting(HbaRateLimitPolicies.AuthAttempt);

        anonymous.MapPost("/login", async (
            LoginDto body,
            IdentityService.IdentityServiceClient identity,
            CancellationToken cancellationToken) =>
        {
            var pair = await identity.LoginWithPasswordAsync(
                new LoginWithPasswordRequest
                {
                    Login = body.Login,
                    Password = body.Password,
                    DeviceId = body.DeviceId ?? string.Empty,
                },
                cancellationToken: cancellationToken);

            return Results.Ok(pair);
        });

        anonymous.MapPost("/refresh", async (
            WebRefreshDto body,
            IdentityService.IdentityServiceClient identity,
            CancellationToken cancellationToken) =>
        {
            var pair = await identity.RefreshTokenAsync(
                new RefreshTokenRequest
                {
                    RefreshToken = body.RefreshToken,
                    DeviceId = body.DeviceId ?? string.Empty,
                },
                cancellationToken: cancellationToken);

            return Results.Ok(pair);
        });

        anonymous.MapPost("/logout", async (
            WebRefreshDto body,
            IdentityService.IdentityServiceClient identity,
            CancellationToken cancellationToken) =>
        {
            await identity.RevokeTokenAsync(
                new RevokeTokenRequest { RefreshToken = body.RefreshToken },
                cancellationToken: cancellationToken);

            return Results.NoContent();
        });

        var authenticated = app.MapGroup("/api/web/v1/auth").RequireAuthorization();

        authenticated.MapGet("/me", async (
            IdentityService.IdentityServiceClient identity,
            CancellationToken cancellationToken) =>
        {
            var principal = await identity.GetPrincipalAsync(
                new GetPrincipalRequest(),
                cancellationToken: cancellationToken);

            return Results.Ok(principal);
        });

        authenticated.MapPost("/password", async (
            ChangePasswordDto body,
            HttpContext http,
            IdentityService.IdentityServiceClient identity,
            CancellationToken cancellationToken) =>
        {
            var subject = http.User.FindFirst("sub")?.Value ?? string.Empty;

            await identity.SetPasswordAsync(
                new SetPasswordRequest
                {
                    AccountId = string.IsNullOrWhiteSpace(body.AccountId) ? subject : body.AccountId,
                    CurrentPassword = body.CurrentPassword ?? string.Empty,
                    NewPassword = body.NewPassword,
                },
                cancellationToken: cancellationToken);

            // Changer de mot de passe ferme les autres sessions : l'interface
            // doit renvoyer l'utilisateur vers l'écran de connexion.
            return Results.NoContent();
        });

        authenticated.MapPost("/sessions/revoke-all", async (
            RevokeAllDto body,
            HttpContext http,
            IdentityService.IdentityServiceClient identity,
            CancellationToken cancellationToken) =>
        {
            var subject = http.User.FindFirst("sub")?.Value ?? string.Empty;

            var response = await identity.RevokeAllSessionsAsync(
                new RevokeAllSessionsRequest
                {
                    AccountId = string.IsNullOrWhiteSpace(body.AccountId) ? subject : body.AccountId,
                },
                cancellationToken: cancellationToken);

            return Results.Ok(new { revoked = response.RevokedCount });
        });

        return app;
    }
}

public sealed record LoginDto(string Login, string Password, string? DeviceId);

public sealed record WebRefreshDto(string RefreshToken, string? DeviceId);

public sealed record ChangePasswordDto(string? AccountId, string? CurrentPassword, string NewPassword);

public sealed record RevokeAllDto(string? AccountId);
