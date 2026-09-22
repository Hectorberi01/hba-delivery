using Hba.BuildingBlocks.Security;
using Hba.Contracts.Delivery.V1;
using Hba.Contracts.Dispatch.V1;
using Hba.Contracts.Driver.V1;

namespace Hba.Bff.Web.Endpoints;

/// <summary>
/// Back-office. Chaque action sensible est auditée côté service : auteur, date,
/// motif, TraceId. Le motif est donc exigé dès l'entrée.
/// </summary>
public static class BackOfficeEndpoints
{
    public static IEndpointRouteBuilder MapBackOfficeEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app.MapGroup("/api/admin/v1").RequireAuthorization(HbaPolicies.BackOffice);

        group.MapGet("/deliveries", async (
            DeliveryService.DeliveryServiceClient deliveries,
            CancellationToken cancellationToken,
            int pageSize = 50,
            string? pageToken = null) =>
        {
            var result = await deliveries.ListDeliveriesAsync(
                new ListDeliveriesRequest { PageSize = pageSize, PageToken = pageToken ?? string.Empty },
                cancellationToken: cancellationToken);

            return Results.Ok(result);
        });

        // Clôture forcée : jamais DELIVERED. Le service le refuse de toute façon.
        group.MapPost("/deliveries/{id}/close", async (
            string id,
            CloseDto body,
            DeliveryService.DeliveryServiceClient deliveries,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(body.Reason))
            {
                return Results.BadRequest(new { code = "MISSING_REASON" });
            }

            var target = body.Failed ? DeliveryStatus.Failed : DeliveryStatus.Cancelled;

            var result = await deliveries.AdminCloseDeliveryAsync(
                new AdminCloseDeliveryRequest { DeliveryId = id, TargetStatus = target, Reason = body.Reason },
                cancellationToken: cancellationToken);

            return Results.Ok(result);
        });

        group.MapPost("/deliveries/{id}/reassign", async (
            string id,
            ReassignDto body,
            DispatchService.DispatchServiceClient dispatch,
            CancellationToken cancellationToken) =>
        {
            var result = await dispatch.ForceReassignAsync(
                new ForceReassignRequest
                {
                    DeliveryId = id,
                    Reason = body.Reason,
                    TargetDriverId = body.TargetDriverId ?? string.Empty,
                },
                cancellationToken: cancellationToken);

            return Results.Ok(result);
        });

        var drivers = app.MapGroup("/api/admin/v1/drivers").RequireAuthorization(HbaPolicies.Admin);

        drivers.MapPost("/{driverId}/kyc", async (
            string driverId,
            KycDto body,
            DriverService.DriverServiceClient driverService,
            CancellationToken cancellationToken) =>
        {
            if (!body.Approved && string.IsNullOrWhiteSpace(body.Reason))
            {
                return Results.BadRequest(new { code = "MISSING_REASON" });
            }

            var result = await driverService.ReviewKycAsync(
                new ReviewKycRequest
                {
                    DriverId = driverId,
                    Approved = body.Approved,
                    Reason = body.Reason ?? string.Empty,
                },
                cancellationToken: cancellationToken);

            return Results.Ok(result);
        });

        drivers.MapPost("/{driverId}/suspend", async (
            string driverId,
            SuspendDto body,
            DriverService.DriverServiceClient driverService,
            CancellationToken cancellationToken) =>
        {
            var result = await driverService.SuspendDriverAsync(
                new SuspendDriverRequest { DriverId = driverId, Reason = body.Reason },
                cancellationToken: cancellationToken);

            return Results.Ok(result);
        });

        return app;
    }
}

public sealed record CloseDto(bool Failed, string Reason);

public sealed record ReassignDto(string Reason, string? TargetDriverId);

public sealed record KycDto(bool Approved, string? Reason);

public sealed record SuspendDto(string Reason);
