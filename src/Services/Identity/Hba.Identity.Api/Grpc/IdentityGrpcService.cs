using Grpc.Core;
using Hba.BuildingBlocks.Application.Messaging;
using Hba.BuildingBlocks.Domain;
using Hba.Contracts.Identity.V1;
using Hba.Identity.Application.Accounts;
using Hba.Identity.Application.Authentication;
using Hba.Identity.Application.Partners;
using Hba.Identity.Application.Queries;
using Microsoft.AspNetCore.Authorization;
using DomainOtpIntent = Hba.Identity.Domain.Otp.OtpIntent;
using ProtoAccount = Hba.Contracts.Identity.V1.Account;

namespace Hba.Identity.Api.Grpc;

/// <summary>
/// Entrée synchrone d'Identity. Les méthodes d'authentification sont forcément
/// anonymes — on n'a pas encore de jeton quand on en demande un. Tout le reste
/// exige un jeton valide, et l'autorisation fine est appliquée dans les
/// handlers.
/// </summary>
[Authorize]
public sealed class IdentityGrpcService(IDispatcher dispatcher) : IdentityService.IdentityServiceBase
{
    // ---------------------------------------------------------------- OTP ---

    [AllowAnonymous]
    public override async Task<RequestOtpResponse> RequestOtp(RequestOtpRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        var result = await dispatcher.SendAsync(
            new RequestOtpCommand(request.Phone, ToDomainIntent(request.Intent), Nullify(request.DeviceId)),
            context.CancellationToken).ConfigureAwait(false);

        return new RequestOtpResponse
        {
            ChallengeId = result.ChallengeId.ToString(),
            ExpiresAt = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTimeOffset(result.ExpiresAt),
            RetryAfterSeconds = result.RetryAfterSeconds,
        };
    }

    [AllowAnonymous]
    public override async Task<TokenPair> VerifyOtp(VerifyOtpRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        var view = await dispatcher.SendAsync(
            new VerifyOtpCommand(
                ParseId(request.ChallengeId, "défi"),
                request.Code,
                Nullify(request.DeviceId),
                Nullify(request.DisplayName)),
            context.CancellationToken).ConfigureAwait(false);

        return IdentityProtoMapper.ToProto(view);
    }

    // ----------------------------------------------------- Mot de passe ---

    [AllowAnonymous]
    public override async Task<TokenPair> LoginWithPassword(
        LoginWithPasswordRequest request,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        var view = await dispatcher.SendAsync(
            new LoginWithPasswordCommand(request.Login, request.Password, Nullify(request.DeviceId)),
            context.CancellationToken).ConfigureAwait(false);

        return IdentityProtoMapper.ToProto(view);
    }

    public override async Task<SetPasswordResponse> SetPassword(
        SetPasswordRequest request,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        await dispatcher.SendAsync(
            new SetPasswordCommand(
                ParseId(request.AccountId, "compte"),
                Nullify(request.CurrentPassword),
                request.NewPassword),
            context.CancellationToken).ConfigureAwait(false);

        return new SetPasswordResponse { Ok = true };
    }

    // --------------------------------------------------------- Sessions ---

    [AllowAnonymous]
    public override async Task<TokenPair> RefreshToken(RefreshTokenRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        var view = await dispatcher.SendAsync(
            new RefreshSessionCommand(request.RefreshToken, Nullify(request.DeviceId)),
            context.CancellationToken).ConfigureAwait(false);

        return IdentityProtoMapper.ToProto(view);
    }

    [AllowAnonymous]
    public override async Task<RevokeTokenResponse> RevokeToken(
        RevokeTokenRequest request,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        // Anonyme à dessein : se déconnecter ne doit pas exiger un jeton d'accès
        // encore valide.
        var revoked = await dispatcher.SendAsync(
            new RevokeSessionCommand(request.RefreshToken),
            context.CancellationToken).ConfigureAwait(false);

        return new RevokeTokenResponse { RevokedCount = revoked };
    }

    public override async Task<RevokeTokenResponse> RevokeAllSessions(
        RevokeAllSessionsRequest request,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        var revoked = await dispatcher.SendAsync(
            new RevokeAllSessionsCommand(ParseId(request.AccountId, "compte")),
            context.CancellationToken).ConfigureAwait(false);

        return new RevokeTokenResponse { RevokedCount = revoked };
    }

    // ---------------------------------------------------------- Comptes ---

    public override async Task<Principal> GetPrincipal(GetPrincipalRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        var view = await dispatcher.QueryAsync(
            new GetPrincipalQuery(Nullify(request.SubjectId)),
            context.CancellationToken).ConfigureAwait(false);

        return IdentityProtoMapper.ToProto(view);
    }

    public override async Task<ProtoAccount> CreateBackOfficeAccount(
        CreateBackOfficeAccountRequest request,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        var view = await dispatcher.SendAsync(
            new CreateBackOfficeAccountCommand(
                request.Email,
                request.DisplayName,
                [.. request.Roles],
                request.InitialPassword),
            context.CancellationToken).ConfigureAwait(false);

        return IdentityProtoMapper.ToProto(view);
    }

    public override async Task<ProtoAccount> CreateMerchantAccount(
        CreateMerchantAccountRequest request,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        var view = await dispatcher.SendAsync(
            new CreateMerchantAccountCommand(
                request.MerchantId,
                Nullify(request.Email),
                Nullify(request.Phone),
                request.DisplayName,
                request.Role,
                request.InitialPassword),
            context.CancellationToken).ConfigureAwait(false);

        return IdentityProtoMapper.ToProto(view);
    }

    public override async Task<ProtoAccount> SetAccountRoles(
        SetAccountRolesRequest request,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        var view = await dispatcher.SendAsync(
            new SetAccountRolesCommand(ParseId(request.AccountId, "compte"), [.. request.Roles], request.Reason),
            context.CancellationToken).ConfigureAwait(false);

        return IdentityProtoMapper.ToProto(view);
    }

    public override async Task<ProtoAccount> SuspendAccount(
        SuspendAccountRequest request,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        var view = await dispatcher.SendAsync(
            new SuspendAccountCommand(ParseId(request.AccountId, "compte"), request.Reason),
            context.CancellationToken).ConfigureAwait(false);

        return IdentityProtoMapper.ToProto(view);
    }

    public override async Task<ProtoAccount> ReactivateAccount(
        ReactivateAccountRequest request,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        var view = await dispatcher.SendAsync(
            new ReactivateAccountCommand(ParseId(request.AccountId, "compte"), request.Reason),
            context.CancellationToken).ConfigureAwait(false);

        return IdentityProtoMapper.ToProto(view);
    }

    // ------------------------------------------------------ Partenaires ---

    public override async Task<PartnerClientCreated> CreatePartnerClient(
        CreatePartnerClientRequest request,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        var view = await dispatcher.SendAsync(
            new CreatePartnerClientCommand(
                request.PartnerName,
                request.Source,
                Nullify(request.WebhookUrl),
                [.. request.Scopes],
                request.RateLimitPerMinute == 0 ? 120 : request.RateLimitPerMinute),
            context.CancellationToken).ConfigureAwait(false);

        return IdentityProtoMapper.ToProto(view);
    }

    public override async Task<PartnerClientCreated> RotatePartnerSecret(
        RotatePartnerSecretRequest request,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        var view = await dispatcher.SendAsync(
            new RotatePartnerSecretCommand(ParseId(request.PartnerId, "partenaire"), request.Reason),
            context.CancellationToken).ConfigureAwait(false);

        return IdentityProtoMapper.ToProto(view);
    }

    [AllowAnonymous]
    public override async Task<TokenPair> IssuePartnerToken(
        IssuePartnerTokenRequest request,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        var view = await dispatcher.SendAsync(
            new IssuePartnerTokenCommand(request.ClientId, request.ClientSecret, [.. request.Scopes]),
            context.CancellationToken).ConfigureAwait(false);

        return IdentityProtoMapper.ToProto(view);
    }

    public override async Task<PartnerWebhookConfig> GetPartnerWebhookConfig(
        GetPartnerWebhookConfigRequest request,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        var view = await dispatcher.QueryAsync(
            new GetPartnerWebhookConfigQuery(ParseId(request.PartnerId, "partenaire")),
            context.CancellationToken).ConfigureAwait(false);

        return new PartnerWebhookConfig
        {
            PartnerId = view.PartnerId,
            WebhookUrl = view.WebhookUrl ?? string.Empty,
            SigningSecret = view.SigningSecret,
            RateLimitPerMinute = view.RateLimitPerMinute,
            Enabled = view.Enabled,
        };
    }

    // ------------------------------------------------------------ Outils ---

    private static DomainOtpIntent ToDomainIntent(OtpIntent intent) => intent switch
    {
        OtpIntent.Driver => DomainOtpIntent.Driver,
        _ => DomainOtpIntent.Customer,
    };

    private static Guid ParseId(string value, string label)
        => Guid.TryParse(value, out var id)
            ? id
            : throw new DomainException("INVALID_ID", $"Identifiant de {label} invalide : {value}.");

    private static string? Nullify(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
