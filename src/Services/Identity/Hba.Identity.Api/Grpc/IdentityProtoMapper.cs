using Google.Protobuf.WellKnownTypes;
using Hba.Contracts.Identity.V1;
using Hba.Identity.Application.Views;
using DomainAccountStatus = Hba.Identity.Domain.Accounts.AccountStatus;
using ProtoAccount = Hba.Contracts.Identity.V1.Account;

namespace Hba.Identity.Api.Grpc;

internal static class IdentityProtoMapper
{
    public static Principal ToProto(PrincipalView view)
    {
        ArgumentNullException.ThrowIfNull(view);

        var principal = new Principal
        {
            SubjectId = view.SubjectId,
            Phone = view.Phone ?? string.Empty,
            Email = view.Email ?? string.Empty,
            DisplayName = view.DisplayName,
            MerchantId = view.MerchantId ?? string.Empty,
            PartnerId = view.PartnerId ?? string.Empty,
            DriverId = view.DriverId ?? string.Empty,
        };

        principal.Roles.AddRange(view.Roles);
        return principal;
    }

    public static TokenPair ToProto(TokenPairView view)
    {
        ArgumentNullException.ThrowIfNull(view);

        return new TokenPair
        {
            AccessToken = view.AccessToken,
            RefreshToken = view.RefreshToken,
            ExpiresInSeconds = view.ExpiresInSeconds,
            TokenType = view.TokenType,
            Principal = ToProto(view.Principal),
        };
    }

    public static ProtoAccount ToProto(AccountView view)
    {
        ArgumentNullException.ThrowIfNull(view);

        var account = new ProtoAccount
        {
            Id = view.Id.ToString(),
            Phone = view.Phone ?? string.Empty,
            Email = view.Email ?? string.Empty,
            DisplayName = view.DisplayName,
            Status = ToProto(view.Status),
            MerchantId = view.MerchantId ?? string.Empty,
            DriverId = view.DriverId ?? string.Empty,
            HasPassword = view.HasPassword,
            StatusReason = view.StatusReason ?? string.Empty,
            CreatedAt = Timestamp.FromDateTimeOffset(view.CreatedAt),
        };

        account.Roles.AddRange(view.Roles);

        if (view.LastLoginAt is not null)
        {
            account.LastLoginAt = Timestamp.FromDateTimeOffset(view.LastLoginAt.Value);
        }

        return account;
    }

    public static PartnerClientCreated ToProto(PartnerSecretsView view)
    {
        ArgumentNullException.ThrowIfNull(view);

        var message = new PartnerClientCreated
        {
            PartnerId = view.PartnerId,
            ClientId = view.ClientId,
            ClientSecret = view.ClientSecret,
            WebhookSigningSecret = view.WebhookSigningSecret,
            CreatedAt = Timestamp.FromDateTimeOffset(view.CreatedAt),
        };

        message.Scopes.AddRange(view.Scopes);
        return message;
    }

    private static Contracts.Identity.V1.AccountStatus ToProto(DomainAccountStatus status) => status switch
    {
        DomainAccountStatus.Active => Contracts.Identity.V1.AccountStatus.Active,
        DomainAccountStatus.Suspended => Contracts.Identity.V1.AccountStatus.Suspended,
        _ => Contracts.Identity.V1.AccountStatus.Unspecified,
    };
}
