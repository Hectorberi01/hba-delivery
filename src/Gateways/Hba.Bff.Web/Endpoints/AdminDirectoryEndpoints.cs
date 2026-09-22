using Hba.BuildingBlocks.Security;
using Hba.Contracts.Directory.V1;
using Hba.Contracts.Identity.V1;

namespace Hba.Bff.Web.Endpoints;

/// <summary>
/// Référencement des commerçants, comptes et clients partenaires. Tout passe
/// par le back-office : personne ne s'inscrit soi-même comme commerce, et un
/// client OAuth ne se crée pas depuis une application.
/// </summary>
public static class AdminDirectoryEndpoints
{
    public static IEndpointRouteBuilder MapAdminDirectoryEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var merchants = app.MapGroup("/api/admin/v1/merchants").RequireAuthorization(HbaPolicies.BackOffice);

        merchants.MapGet("", async (
            DirectoryService.DirectoryServiceClient directory,
            CancellationToken cancellationToken,
            string? query = null,
            bool onlyActive = false,
            int pageSize = 25,
            int offset = 0) =>
        {
            var response = await directory.ListMerchantsAsync(
                new ListMerchantsRequest
                {
                    Query = query ?? string.Empty,
                    OnlyActive = onlyActive,
                    PageSize = pageSize,
                    Offset = offset,
                },
                cancellationToken: cancellationToken);

            return Results.Ok(response);
        });

        merchants.MapGet("/{merchantId}", async (
            string merchantId,
            DirectoryService.DirectoryServiceClient directory,
            CancellationToken cancellationToken) =>
        {
            var merchant = await directory.GetMerchantAsync(
                new GetMerchantRequest { MerchantId = merchantId },
                cancellationToken: cancellationToken);

            return Results.Ok(merchant);
        });

        merchants.MapPost("", async (
            CreateMerchantDto body,
            DirectoryService.DirectoryServiceClient directory,
            CancellationToken cancellationToken) =>
        {
            var merchant = await directory.CreateMerchantAsync(
                new CreateMerchantRequest
                {
                    LegalName = body.LegalName,
                    ContactName = body.ContactName ?? string.Empty,
                    ContactPhone = body.ContactPhone,
                    ContactEmail = body.ContactEmail ?? string.Empty,
                    AveragePreparationMinutes = body.AveragePreparationMinutes,
                },
                cancellationToken: cancellationToken);

            return Results.Created($"/api/admin/v1/merchants/{merchant.Id}", merchant);
        });

        var accounts = app.MapGroup("/api/admin/v1/accounts").RequireAuthorization(HbaPolicies.BackOffice);

        accounts.MapPost("/back-office", async (
            CreateBackOfficeAccountDto body,
            IdentityService.IdentityServiceClient identity,
            CancellationToken cancellationToken) =>
        {
            var request = new CreateBackOfficeAccountRequest
            {
                Email = body.Email,
                DisplayName = body.DisplayName,
                InitialPassword = body.InitialPassword,
            };

            request.Roles.AddRange(body.Roles);

            var account = await identity.CreateBackOfficeAccountAsync(request, cancellationToken: cancellationToken);

            return Results.Created($"/api/admin/v1/accounts/{account.Id}", account);
        });

        accounts.MapPost("/merchant", async (
            CreateMerchantAccountDto body,
            IdentityService.IdentityServiceClient identity,
            CancellationToken cancellationToken) =>
        {
            var account = await identity.CreateMerchantAccountAsync(
                new CreateMerchantAccountRequest
                {
                    MerchantId = body.MerchantId,
                    Email = body.Email ?? string.Empty,
                    Phone = body.Phone ?? string.Empty,
                    DisplayName = body.DisplayName,
                    Role = body.Role,
                    InitialPassword = body.InitialPassword,
                },
                cancellationToken: cancellationToken);

            return Results.Created($"/api/admin/v1/accounts/{account.Id}", account);
        });

        accounts.MapPost("/{accountId}/suspend", async (
            string accountId,
            ReasonDto body,
            IdentityService.IdentityServiceClient identity,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(body.Reason))
            {
                return Results.BadRequest(new { code = "MISSING_REASON" });
            }

            var account = await identity.SuspendAccountAsync(
                new SuspendAccountRequest { AccountId = accountId, Reason = body.Reason },
                cancellationToken: cancellationToken);

            return Results.Ok(account);
        });

        accounts.MapPost("/{accountId}/reactivate", async (
            string accountId,
            ReasonDto body,
            IdentityService.IdentityServiceClient identity,
            CancellationToken cancellationToken) =>
        {
            var account = await identity.ReactivateAccountAsync(
                new ReactivateAccountRequest { AccountId = accountId, Reason = body.Reason ?? string.Empty },
                cancellationToken: cancellationToken);

            return Results.Ok(account);
        });

        // La création d'un client partenaire exige le rôle admin, pas seulement
        // le back-office : elle produit des secrets.
        var partners = app.MapGroup("/api/admin/v1/partners").RequireAuthorization(HbaPolicies.Admin);

        partners.MapPost("", async (
            CreatePartnerDto body,
            IdentityService.IdentityServiceClient identity,
            CancellationToken cancellationToken) =>
        {
            var request = new CreatePartnerClientRequest
            {
                PartnerName = body.PartnerName,
                Source = body.Source,
                WebhookUrl = body.WebhookUrl ?? string.Empty,
                RateLimitPerMinute = body.RateLimitPerMinute ?? 120,
            };

            request.Scopes.AddRange(body.Scopes ?? []);

            var created = await identity.CreatePartnerClientAsync(request, cancellationToken: cancellationToken);

            // Les deux secrets ne sont lisibles qu'ici : l'interface doit le dire
            // clairement, ils ne seront plus jamais affichés.
            return Results.Created($"/api/admin/v1/partners/{created.PartnerId}", created);
        });

        partners.MapPost("/{partnerId}/rotate-secret", async (
            string partnerId,
            ReasonDto body,
            IdentityService.IdentityServiceClient identity,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(body.Reason))
            {
                return Results.BadRequest(new { code = "MISSING_REASON" });
            }

            var rotated = await identity.RotatePartnerSecretAsync(
                new RotatePartnerSecretRequest { PartnerId = partnerId, Reason = body.Reason },
                cancellationToken: cancellationToken);

            return Results.Ok(rotated);
        });

        return app;
    }
}

public sealed record CreateMerchantDto(
    string LegalName,
    string? ContactName,
    string ContactPhone,
    string? ContactEmail,
    int AveragePreparationMinutes);

public sealed record CreateBackOfficeAccountDto(
    string Email,
    string DisplayName,
    IReadOnlyList<string> Roles,
    string InitialPassword);

public sealed record CreateMerchantAccountDto(
    string MerchantId,
    string? Email,
    string? Phone,
    string DisplayName,
    string Role,
    string InitialPassword);

public sealed record ReasonDto(string? Reason);

public sealed record CreatePartnerDto(
    string PartnerName,
    string Source,
    string? WebhookUrl,
    IReadOnlyList<string>? Scopes,
    int? RateLimitPerMinute);
