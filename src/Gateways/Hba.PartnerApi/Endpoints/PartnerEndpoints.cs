using Hba.BuildingBlocks.Security;
using Hba.Contracts.Common.V1;
using Hba.Contracts.Delivery.V1;
using Hba.Contracts.Pricing.V1;

namespace Hba.PartnerApi.Endpoints;

/// <summary>
/// API publique versionnée. Le contrat fait foi : HBA Delivery ne dépend jamais
/// des modèles internes d'un partenaire, et un partenaire ne voit jamais les
/// livraisons d'un autre — le cloisonnement est appliqué par les services, à
/// partir du claim partner_id.
/// </summary>
public static class PartnerEndpoints
{
    public static IEndpointRouteBuilder MapPartnerEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app.MapGroup("/api/v1").RequireAuthorization(HbaPolicies.Partner);

        group.MapPost("/quotes", async (
            PartnerQuoteRequest body,
            PricingService.PricingServiceClient pricing,
            CancellationToken cancellationToken) =>
        {
            var quote = await pricing.GetQuoteAsync(
                new GetQuoteRequest
                {
                    Pickup = new GeoPoint { Latitude = body.Pickup.Latitude, Longitude = body.Pickup.Longitude },
                    Dropoff = new GeoPoint { Latitude = body.Dropoff.Latitude, Longitude = body.Dropoff.Longitude },
                    VehicleType = VehicleType.Motorcycle,
                    Source = Source.PartnerApi,
                    PackageWeightGrams = body.PackageWeightGrams,
                },
                cancellationToken: cancellationToken);

            return Results.Ok(new
            {
                quoteId = quote.Id,
                totalXof = quote.Total?.Amount ?? 0,
                distanceMeters = quote.DistanceMeters,
                expiresAt = quote.ExpiresAt?.ToDateTimeOffset(),
            });
        });

        // Idempotency-Key est OBLIGATOIRE : un partenaire qui rejoue sa requête ne
        // doit jamais créer une seconde course.
        group.MapPost("/deliveries", async (
            PartnerCreateDeliveryRequest body,
            HttpContext http,
            DeliveryService.DeliveryServiceClient deliveries,
            CancellationToken cancellationToken) =>
        {
            var idempotencyKey = http.Request.Headers["Idempotency-Key"].FirstOrDefault();

            if (string.IsNullOrWhiteSpace(idempotencyKey))
            {
                return Results.BadRequest(new
                {
                    code = "MISSING_IDEMPOTENCY_KEY",
                    message = "L'en-tête Idempotency-Key est obligatoire.",
                });
            }

            if (string.IsNullOrWhiteSpace(body.ExternalOrderId))
            {
                return Results.BadRequest(new
                {
                    code = "MISSING_EXTERNAL_ORDER_ID",
                    message = "externalOrderId est obligatoire.",
                });
            }

            var response = await deliveries.CreateDeliveryAsync(
                new CreateDeliveryRequest
                {
                    IdempotencyKey = idempotencyKey,
                    QuoteId = body.QuoteId,
                    Source = Source.PartnerApi,
                    ExternalOrderId = body.ExternalOrderId,
                    MerchantId = body.MerchantId ?? string.Empty,
                    PickupPointId = body.PickupPointId ?? string.Empty,
                    Pickup = ToLocation(body.Pickup),
                    Dropoff = ToLocation(body.Dropoff),
                    RecipientName = body.Recipient.Name,
                    RecipientPhone = body.Recipient.Phone,
                    PackageDescription = body.PackageDescription ?? string.Empty,
                    PackageWeightGrams = body.PackageWeightGrams,
                },
                cancellationToken: cancellationToken);

            return Results.Created($"/api/v1/deliveries/{response.Delivery.Id}", response.Delivery);
        });

        group.MapGet("/deliveries/{id}", async (
            string id,
            DeliveryService.DeliveryServiceClient deliveries,
            CancellationToken cancellationToken) =>
        {
            var delivery = await deliveries.GetDeliveryAsync(
                new GetDeliveryRequest { DeliveryId = id },
                cancellationToken: cancellationToken);

            return Results.Ok(delivery);
        });

        group.MapPost("/deliveries/{id}/cancel", async (
            string id,
            PartnerCancelRequest body,
            DeliveryService.DeliveryServiceClient deliveries,
            CancellationToken cancellationToken) =>
        {
            var delivery = await deliveries.CancelDeliveryAsync(
                new CancelDeliveryRequest { DeliveryId = id, Reason = body.Reason },
                cancellationToken: cancellationToken);

            return Results.Ok(delivery);
        });

        return app;
    }

    private static Location ToLocation(PartnerLocation dto) => new()
    {
        Point = new GeoPoint { Latitude = dto.Latitude, Longitude = dto.Longitude },
        Landmark = dto.Landmark,
        Phone = dto.Phone,
        ContactName = dto.ContactName,
        Notes = dto.Notes ?? string.Empty,
    };
}

public sealed record PartnerLocation(
    double Latitude,
    double Longitude,
    string Landmark,
    string Phone,
    string ContactName,
    string? Notes);

public sealed record PartnerRecipient(string Name, string Phone);

public sealed record PartnerQuoteRequest(
    PartnerLocation Pickup,
    PartnerLocation Dropoff,
    int PackageWeightGrams);

public sealed record PartnerCreateDeliveryRequest(
    string QuoteId,
    string ExternalOrderId,
    PartnerLocation Pickup,
    PartnerLocation Dropoff,
    PartnerRecipient Recipient,
    string? MerchantId,
    string? PickupPointId,
    string? PackageDescription,
    int PackageWeightGrams);

public sealed record PartnerCancelRequest(string Reason);
