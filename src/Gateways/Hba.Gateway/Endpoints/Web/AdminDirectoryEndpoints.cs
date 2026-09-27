using Hba.BuildingBlocks.Security;
using Hba.Contracts.Delivery.V1;
using Hba.Contracts.Directory.V1;
using Hba.Contracts.Identity.V1;

namespace Hba.Gateway.Endpoints.Web;

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

        // L'ANNUAIRE DES CLIENTS N'EST PAS « LE BACK-OFFICE ». Sa politique
        // exclut finance ; le service Directory reverifie et met en forme la
        // reponse selon le role, parce que la passerelle ne saurait pas quoi
        // masquer. Voir DirectoryAccess.EnsureCanReadCustomers.
        var customers = app.MapGroup("/api/admin/v1/customers")
            .RequireAuthorization(HbaPolicies.AnnuaireClients);

        customers.MapGet("", async (
            DirectoryService.DirectoryServiceClient directory,
            CancellationToken cancellationToken,
            string? query = null,
            int pageSize = 25,
            int offset = 0) =>
        {
            var response = await directory.ListCustomersAsync(
                new ListCustomersRequest
                {
                    Query = query ?? string.Empty,
                    PageSize = pageSize,
                    Offset = offset,
                },
                cancellationToken: cancellationToken);

            return Results.Ok(new
            {
                total = response.Total,
                customers = response.Customers.Select(c => new
                {
                    customerId = c.CustomerId,
                    displayName = c.DisplayName,
                    phoneMasked = c.PhoneMasked,
                    createdAt = c.CreatedAt?.ToDateTimeOffset(),
                }),
            });
        });

        customers.MapGet("/{customerId}", async (
            string customerId,
            DirectoryService.DirectoryServiceClient directory,
            CancellationToken cancellationToken) =>
        {
            var fiche = await directory.GetCustomerFileAsync(
                new GetCustomerFileRequest { CustomerId = customerId },
                cancellationToken: cancellationToken);

            // MISE EN FORME PLUTOT QUE RELAIS BRUT, comme pour les positions :
            // System.Text.Json rendrait les dates en { seconds, nanos }.
            return Results.Ok(new
            {
                customerId = fiche.CustomerId,
                displayName = fiche.DisplayName,
                phone = fiche.Phone,
                email = string.IsNullOrEmpty(fiche.Email) ? null : fiche.Email,
                emailHidden = fiche.EmailHidden,
                addressesHidden = fiche.AddressesHidden,
                createdAt = fiche.CreatedAt?.ToDateTimeOffset(),
                favoriteAddresses = fiche.FavoriteAddresses.Select(a => new
                {
                    id = a.Id,
                    label = a.Label,
                    isDefault = a.IsDefault,
                    landmark = a.Address?.Location?.Landmark,
                    contactName = a.Address?.Location?.ContactName,
                    phone = a.Address?.Location?.Phone,
                    notes = string.IsNullOrEmpty(a.Address?.Location?.Notes)
                        ? null
                        : a.Address.Location.Notes,
                }),
            });
        });

        // LE CUMUL FACTURE A SA PROPRE POLITIQUE. Ops et support ouvrent la
        // fiche et voient les courses une par une ; le total en argent est
        // reserve a l'administration. Le service Delivery reverifie.
        app.MapGet("/api/admin/v1/customers/{customerId}/billing", async (
                string customerId,
                DeliveryService.DeliveryServiceClient deliveries,
                CancellationToken cancellationToken) =>
            {
                var facture = await deliveries.GetCustomerBillingAsync(
                    new GetCustomerBillingRequest { CustomerId = customerId },
                    cancellationToken: cancellationToken);

                return Results.Ok(new
                {
                    customerId = facture.CustomerId,
                    deliveredCount = facture.DeliveredCount,
                    billedTotalXof = facture.BilledTotalXof,
                });
            })
            .RequireAuthorization(HbaPolicies.Admin);

        // LE JOURNAL DES ACCES, A L'ADMINISTRATION SEULE. Ops et support y
        // figurent : leur en donner la main reviendrait a les laisser
        // verifier ce qu'on sait d'eux.
        app.MapGet("/api/admin/v1/customers/{customerId}/access-log", async (
                string customerId,
                DirectoryService.DirectoryServiceClient directory,
                CancellationToken cancellationToken,
                int limit = 50) =>
            {
                var log = await directory.GetCustomerAccessLogAsync(
                    new GetCustomerAccessLogRequest { CustomerId = customerId, Limit = limit },
                    cancellationToken: cancellationToken);

                return Results.Ok(new
                {
                    entries = log.Entries.Select(e => new
                    {
                        readerId = e.ReaderId,
                        readerRoles = e.ReaderRoles,
                        readAt = e.ReadAt?.ToDateTimeOffset(),
                    }),
                });
            })
            .RequireAuthorization(HbaPolicies.Admin);

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
