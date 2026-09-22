using Hba.BuildingBlocks.Security;
using Hba.Contracts.Common.V1;
using Hba.Contracts.Delivery.V1;
using Hba.Contracts.Dispatch.V1;
using Hba.Contracts.Driver.V1;

namespace Hba.Bff.Driver.Endpoints;

/// <summary>
/// Surface REST de l'app livreur. Chaque action mutante accepte un horodatage
/// client et une Idempotency-Key : l'app met ses actions en file quand la
/// connexion tombe et les rejoue telles quelles au retour du réseau.
/// </summary>
public static class DriverEndpoints
{
    public static IEndpointRouteBuilder MapDriverEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app.MapGroup("/api/driver/v1").RequireAuthorization(HbaPolicies.Driver);

        group.MapPost("/presence/online", async (
            PositionDto body,
            HttpContext http,
            DriverService.DriverServiceClient drivers,
            CancellationToken cancellationToken) =>
        {
            var driver = await drivers.GoOnlineAsync(
                new GoOnlineRequest
                {
                    DriverId = DriverIdOf(http),
                    Position = new GeoPoint { Latitude = body.Latitude, Longitude = body.Longitude },
                },
                cancellationToken: cancellationToken);

            return Results.Ok(driver);
        });

        group.MapPost("/presence/offline", async (
            HttpContext http,
            DriverService.DriverServiceClient drivers,
            CancellationToken cancellationToken) =>
        {
            var driver = await drivers.GoOfflineAsync(
                new GoOfflineRequest { DriverId = DriverIdOf(http) },
                cancellationToken: cancellationToken);

            return Results.Ok(driver);
        });

        group.MapPost("/position", async (
            PositionDto body,
            HttpContext http,
            DriverService.DriverServiceClient drivers,
            CancellationToken cancellationToken) =>
        {
            await drivers.UpdateLocationAsync(
                new UpdateLocationRequest
                {
                    DriverId = DriverIdOf(http),
                    Position = new GeoPoint { Latitude = body.Latitude, Longitude = body.Longitude },
                    CapturedAt = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTimeOffset(
                        body.CapturedAt ?? DateTimeOffset.UtcNow),
                },
                cancellationToken: cancellationToken);

            return Results.NoContent();
        });

        group.MapGet("/offers/current", async (
            HttpContext http,
            DispatchService.DispatchServiceClient dispatch,
            CancellationToken cancellationToken) =>
        {
            var offer = await dispatch.GetCurrentOfferAsync(
                new GetCurrentOfferRequest { DriverId = DriverIdOf(http) },
                cancellationToken: cancellationToken);

            return Results.Ok(offer);
        });

        group.MapPost("/offers/{offerId}/accept", async (
            string offerId,
            HttpContext http,
            DispatchService.DispatchServiceClient dispatch,
            CancellationToken cancellationToken) =>
        {
            var result = await dispatch.AcceptOfferAsync(
                new AcceptOfferRequest
                {
                    OfferId = offerId,
                    IdempotencyKey = IdempotencyKeyOf(http),
                },
                cancellationToken: cancellationToken);

            // Perdre une offre n'est pas une erreur : c'est le fonctionnement
            // normal d'une vague. L'app doit pouvoir l'afficher sans alarmer.
            return result.Accepted
                ? Results.Ok(result.Offer)
                : Results.Conflict(new { code = result.RejectionCode });
        });

        group.MapPost("/offers/{offerId}/decline", async (
            string offerId,
            DeclineDto body,
            DispatchService.DispatchServiceClient dispatch,
            CancellationToken cancellationToken) =>
        {
            await dispatch.DeclineOfferAsync(
                new DeclineOfferRequest { OfferId = offerId, Reason = body.Reason ?? string.Empty },
                cancellationToken: cancellationToken);

            return Results.NoContent();
        });

        group.MapGet("/missions", async (
            DeliveryService.DeliveryServiceClient deliveries,
            CancellationToken cancellationToken) =>
        {
            var result = await deliveries.ListDeliveriesAsync(
                new ListDeliveriesRequest { PageSize = 25 },
                cancellationToken: cancellationToken);

            return Results.Ok(result);
        });

        group.MapPost("/missions/{id}/arrived", async (
            string id,
            TimestampedDto body,
            HttpContext http,
            DeliveryService.DeliveryServiceClient deliveries,
            CancellationToken cancellationToken) =>
        {
            var result = await deliveries.MarkArrivedAtPickupAsync(
                new MarkArrivedAtPickupRequest
                {
                    DeliveryId = id,
                    OccurredAt = ToTimestamp(body.OccurredAt),
                    IdempotencyKey = IdempotencyKeyOf(http),
                },
                cancellationToken: cancellationToken);

            return Results.Ok(result);
        });

        group.MapPost("/missions/{id}/picked-up", async (
            string id,
            ProofDto body,
            HttpContext http,
            DeliveryService.DeliveryServiceClient deliveries,
            CancellationToken cancellationToken) =>
        {
            var result = await deliveries.MarkPickedUpAsync(
                new MarkPickedUpRequest
                {
                    DeliveryId = id,
                    OccurredAt = ToTimestamp(body.OccurredAt),
                    IdempotencyKey = IdempotencyKeyOf(http),
                    ProofObjectKey = body.ProofObjectKey ?? string.Empty,
                },
                cancellationToken: cancellationToken);

            return Results.Ok(result);
        });

        // Le code vient du destinataire. Le livreur le saisit : il ne l'a jamais reçu.
        group.MapPost("/missions/{id}/deliver", async (
            string id,
            ConfirmDto body,
            HttpContext http,
            DeliveryService.DeliveryServiceClient deliveries,
            CancellationToken cancellationToken) =>
        {
            var result = await deliveries.ConfirmDeliveryAsync(
                new ConfirmDeliveryRequest
                {
                    DeliveryId = id,
                    Otp = body.Otp,
                    OccurredAt = ToTimestamp(body.OccurredAt),
                    IdempotencyKey = IdempotencyKeyOf(http),
                    ProofObjectKey = body.ProofObjectKey ?? string.Empty,
                },
                cancellationToken: cancellationToken);

            return Results.Ok(result);
        });

        return app;
    }

    private static string DriverIdOf(HttpContext http)
        => http.User.FindFirst(HbaClaims.DriverId)?.Value
           ?? throw new InvalidOperationException("Le jeton livreur ne porte pas driver_id.");

    private static string IdempotencyKeyOf(HttpContext http)
        => http.Request.Headers["Idempotency-Key"].FirstOrDefault() ?? string.Empty;

    private static Google.Protobuf.WellKnownTypes.Timestamp? ToTimestamp(DateTimeOffset? value)
        => value is null ? null : Google.Protobuf.WellKnownTypes.Timestamp.FromDateTimeOffset(value.Value);
}

public sealed record PositionDto(double Latitude, double Longitude, DateTimeOffset? CapturedAt);

public sealed record DeclineDto(string? Reason);

public sealed record TimestampedDto(DateTimeOffset? OccurredAt);

public sealed record ProofDto(DateTimeOffset? OccurredAt, string? ProofObjectKey);

public sealed record ConfirmDto(string Otp, DateTimeOffset? OccurredAt, string? ProofObjectKey);
