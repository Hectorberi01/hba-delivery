using Hba.BuildingBlocks.Application.Messaging;
using Hba.Identity.Application.Views;
using Hba.Identity.Domain.Otp;

namespace Hba.Identity.Application.Authentication;

public sealed record RequestOtpCommand(string Phone, OtpIntent Intent, string? DeviceId)
    : ICommand<RequestOtpResult>;

/// <summary>
/// Volontairement identique dans tous les cas : le numéro soit inconnu, connu,
/// suspendu ou rattaché à un compte à mot de passe ne change rien à ce qui est
/// renvoyé.
/// </summary>
public sealed record RequestOtpResult(
    Guid ChallengeId,
    DateTimeOffset ExpiresAt,
    int RetryAfterSeconds);

public sealed record VerifyOtpCommand(Guid ChallengeId, string Code, string? DeviceId, string? DisplayName)
    : ICommand<TokenPairView>;

public sealed record LoginWithPasswordCommand(string Login, string Password, string? DeviceId)
    : ICommand<TokenPairView>;

public sealed record RefreshSessionCommand(string RefreshToken, string? DeviceId) : ICommand<TokenPairView>;

public sealed record RevokeSessionCommand(string RefreshToken) : ICommand<int>;

public sealed record RevokeAllSessionsCommand(Guid AccountId) : ICommand<int>;
