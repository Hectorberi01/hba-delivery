using Hba.BuildingBlocks.Grpc;
using Hba.BuildingBlocks.Security;
using Hba.Contracts.Common.V1;
using Hba.Contracts.Delivery.V1;
using Hba.Contracts.Pricing.V1;

namespace Hba.Bff.Client.Endpoints;

/// <summary>
/// Surface REST de l'app client. Elle ne fait que traduire : aucune règle
/// métier, aucune décision d'autorisation au-delà de l'exigence du rôle.
/// </summary>
public static class ClientDeliveryEndpoints
{
    public static IEndpointRouteBuilder MapClientDeliveryEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app.MapGroup("/api/client/v1").RequireAuthorization(HbaPolicies.Customer);

        group.MapPost("/quotes", async (
            QuoteRequestDto body,
            PricingService.PricingServiceClient pricing,
            CancellationToken cancellationToken) =>
        {
            var quote = await pricing.GetQuoteAsync(
                new GetQuoteRequest
                {
                    Pickup = new GeoPoint { Latitude = body.PickupLatitude, Longitude = body.PickupLongitude },
                    Dropoff = new GeoPoint { Latitude = body.DropoffLatitude, Longitude = body.DropoffLongitude },
                    VehicleType = VehicleType.Motorcycle,
                    Source = Source.ClientApp,
                    PackageWeightGrams = body.PackageWeightGrams,
                },
                cancellationToken: cancellationToken);

            return Results.Ok(new
            {
                quoteId = quote.Id,
                totalXof = quote.Total?.Amount ?? 0,
                distanceMeters = quote.DistanceMeters,
                durationSeconds = quote.DurationSeconds,
                expiresAt = quote.ExpiresAt?.ToDateTimeOffset(),
            });
        });

        group.MapPost("/deliveries", async (
            CreateDeliveryDto body,
            HttpContext http,
            DeliveryService.DeliveryServiceClient delivery,
            CancellationToken cancellationToken) =>
        {
            var request = new CreateDeliveryRequest
            {
                IdempotencyKey = http.Request.Headers["Idempotency-Key"].FirstOrDefault() ?? string.Empty,
                QuoteId = body.QuoteId,
                Source = Source.ClientApp,
                MerchantId = body.MerchantId ?? string.Empty,
                PickupPointId = body.PickupPointId ?? string.Empty,
                Pickup = ToLocation(body.Pickup),
                Dropoff = ToLocation(body.Dropoff),
                RecipientName = body.RecipientName,
                RecipientPhone = body.RecipientPhone,
                PackageDescription = body.PackageDescription ?? string.Empty,
                PackageWeightGrams = body.PackageWeightGrams,
            };

            var response = await delivery.CreateDeliveryAsync(request, cancellationToken: cancellationToken);

            return Results.Created(
                $"/api/client/v1/deliveries/{response.Delivery.Id}",
                new
                {
                    delivery = response.Delivery,
                    payment = new
                    {
                        intentId = response.PaymentIntentId,
                        redirectUrl = response.PaymentRedirectUrl,
                    },
                });
        });

        group.MapGet("/deliveries/{id}", async (
            string id,
            DeliveryService.DeliveryServiceClient delivery,
            CancellationToken cancellationToken) =>
        {
            var result = await delivery.GetDeliveryAsync(
                new GetDeliveryRequest { DeliveryId = id },
                cancellationToken: cancellationToken);

            return Results.Ok(result);
        });

        group.MapGet("/deliveries", async (
            DeliveryService.DeliveryServiceClient delivery,
            CancellationToken cancellationToken,
            int pageSize = 25,
            string? pageToken = null) =>
        {
            var result = await delivery.ListDeliveriesAsync(
                new ListDeliveriesRequest { PageSize = pageSize, PageToken = pageToken ?? string.Empty },
                cancellationToken: cancellationToken);

            return Results.Ok(result);
        });

        group.MapPost("/deliveries/{id}/cancel", async (
            string id,
            CancelDto body,
            DeliveryService.DeliveryServiceClient delivery,
            CancellationToken cancellationToken) =>
        {
            var result = await delivery.CancelDeliveryAsync(
                new CancelDeliveryRequest { DeliveryId = id, Reason = body.Reason },
                cancellationToken: cancellationToken);

            return Results.Ok(result);
        });

        return app;
    }

    private static Location ToLocation(LocationDto dto) => new()
    {
        Point = new GeoPoint { Latitude = dto.Latitude, Longitude = dto.Longitude },
        Landmark = dto.Landmark,
        Phone = dto.Phone,
        ContactName = dto.ContactName,
        Notes = dto.Notes ?? string.Empty,
    };
}

public sealed record QuoteRequestDto(
    double PickupLatitude,
    double PickupLongitude,
    double DropoffLatitude,
    double DropoffLongitude,
    int PackageWeightGrams);

public sealed record LocationDto(
    double Latitude,
    double Longitude,
    string Landmark,
    string Phone,
    string ContactName,
    string? Notes);

public sealed record CreateDeliveryDto(
    string QuoteId,
    LocationDto Pickup,
    LocationDto Dropoff,
    string RecipientName,
    string RecipientPhone,
    string? MerchantId,
    string? PickupPointId,
    string? PackageDescription,
    int PackageWeightGrams);

public sealed record CancelDto(string Reason);
