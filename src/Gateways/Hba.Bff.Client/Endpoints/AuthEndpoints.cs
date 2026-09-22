using Hba.BuildingBlocks.Security;
using Hba.Contracts.Identity.V1;

namespace Hba.Bff.Client.Endpoints;

/// <summary>
/// Obtention et renouvellement du jeton pour l'app client. Ces routes sont
/// forcément anonymes : on n'a pas encore de jeton quand on en demande un.
/// </summary>
public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapClientAuthEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app.MapGroup("/api/client/v1/auth")
            .AllowAnonymous()
            .RequireRateLimiting(HbaRateLimitPolicies.AuthAttempt);

        group.MapPost("/otp/request", async (
            OtpRequestDto body,
            IdentityService.IdentityServiceClient identity,
            CancellationToken cancellationToken) =>
        {
            var response = await identity.RequestOtpAsync(
                new RequestOtpRequest
                {
                    Phone = body.Phone,
                    Intent = OtpIntent.Customer,
                    DeviceId = body.DeviceId ?? string.Empty,
                },
                cancellationToken: cancellationToken);

            return Results.Ok(new
            {
                challengeId = response.ChallengeId,
                expiresAt = response.ExpiresAt?.ToDateTimeOffset(),
                retryAfterSeconds = response.RetryAfterSeconds,
            });
        }).RequireRateLimiting(HbaRateLimitPolicies.OtpRequest);

        group.MapPost("/otp/verify", async (
            OtpVerifyDto body,
            IdentityService.IdentityServiceClient identity,
            CancellationToken cancellationToken) =>
        {
            var pair = await identity.VerifyOtpAsync(
                new VerifyOtpRequest
                {
                    ChallengeId = body.ChallengeId,
                    Code = body.Code,
                    DeviceId = body.DeviceId ?? string.Empty,
                    DisplayName = body.DisplayName ?? string.Empty,
                },
                cancellationToken: cancellationToken);

            return Results.Ok(pair);
        });

        group.MapPost("/refresh", async (
            RefreshDto body,
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

        group.MapPost("/logout", async (
            RefreshDto body,
            IdentityService.IdentityServiceClient identity,
            CancellationToken cancellationToken) =>
        {
            await identity.RevokeTokenAsync(
                new RevokeTokenRequest { RefreshToken = body.RefreshToken },
                cancellationToken: cancellationToken);

            return Results.NoContent();
        });

        return app;
    }
}

public sealed record OtpRequestDto(string Phone, string? DeviceId);

public sealed record OtpVerifyDto(string ChallengeId, string Code, string? DeviceId, string? DisplayName);

public sealed record RefreshDto(string RefreshToken, string? DeviceId);
