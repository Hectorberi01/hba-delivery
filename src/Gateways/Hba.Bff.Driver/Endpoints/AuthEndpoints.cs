using Hba.BuildingBlocks.Security;
using Hba.Contracts.Identity.V1;

namespace Hba.Bff.Driver.Endpoints;

/// <summary>
/// Authentification du livreur : téléphone + code SMS. Le compte naît à la
/// première vérification ; il reste inactif tant que le KYC n'est pas validé,
/// ce que le service Driver contrôle de son côté.
/// </summary>
public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapDriverAuthEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app.MapGroup("/api/driver/v1/auth")
            .AllowAnonymous()
            .RequireRateLimiting(HbaRateLimitPolicies.AuthAttempt);

        group.MapPost("/otp/request", async (
            DriverOtpRequestDto body,
            IdentityService.IdentityServiceClient identity,
            CancellationToken cancellationToken) =>
        {
            var response = await identity.RequestOtpAsync(
                new RequestOtpRequest
                {
                    Phone = body.Phone,
                    Intent = OtpIntent.Driver,
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
            DriverOtpVerifyDto body,
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
            DriverRefreshDto body,
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
            DriverRefreshDto body,
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

public sealed record DriverOtpRequestDto(string Phone, string? DeviceId);

public sealed record DriverOtpVerifyDto(string ChallengeId, string Code, string? DeviceId, string? DisplayName);

public sealed record DriverRefreshDto(string RefreshToken, string? DeviceId);
