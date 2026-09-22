using Hba.BuildingBlocks.Security;
using Hba.Contracts.Delivery.V1;

namespace Hba.Bff.Web.Endpoints;

/// <summary>
/// Portail commerçant. Le périmètre est imposé par le service Delivery à partir
/// du claim merchant_id : ce BFF ne filtre rien lui-même.
/// </summary>
public static class MerchantEndpoints
{
    public static IEndpointRouteBuilder MapMerchantEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app.MapGroup("/api/merchant/v1").RequireAuthorization(HbaPolicies.Merchant);

        group.MapGet("/deliveries", async (
            DeliveryService.DeliveryServiceClient deliveries,
            CancellationToken cancellationToken,
            int pageSize = 25,
            string? pageToken = null) =>
        {
            var result = await deliveries.ListDeliveriesAsync(
                new ListDeliveriesRequest { PageSize = pageSize, PageToken = pageToken ?? string.Empty },
                cancellationToken: cancellationToken);

            return Results.Ok(result);
        });

        group.MapGet("/deliveries/{id}", async (
            string id,
            DeliveryService.DeliveryServiceClient deliveries,
            CancellationToken cancellationToken) =>
        {
            var result = await deliveries.GetDeliveryAsync(
                new GetDeliveryRequest { DeliveryId = id },
                cancellationToken: cancellationToken);

            return Results.Ok(result);
        });

        // L'annulation est réservée au propriétaire : merchant_staff est refusé
        // côté service, pas seulement ici.
        group.MapPost("/deliveries/{id}/cancel", async (
            string id,
            MerchantCancelDto body,
            DeliveryService.DeliveryServiceClient deliveries,
            CancellationToken cancellationToken) =>
        {
            var result = await deliveries.CancelDeliveryAsync(
                new CancelDeliveryRequest { DeliveryId = id, Reason = body.Reason },
                cancellationToken: cancellationToken);

            return Results.Ok(result);
        });

        return app;
    }
}

public sealed record MerchantCancelDto(string Reason);
