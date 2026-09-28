using Hba.BuildingBlocks.Grpc;
using Hba.BuildingBlocks.Security;
using Hba.Contracts.Common.V1;
using Hba.Contracts.Delivery.V1;
using Hba.Contracts.Pricing.V1;

namespace Hba.Gateway.Endpoints.Client;

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
                    delivery = Lisible(response.Delivery),
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

            return Results.Ok(Lisible(result));
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

            return Results.Ok(new
            {
                deliveries = result.Deliveries.Select(Lisible),
                nextPageToken = result.NextPageToken,
            });
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

            return Results.Ok(Lisible(result));
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

    /// <summary>
    /// Traduit une livraison protobuf en JSON QUE L'APPLICATION SAIT LIRE.
    ///
    /// CE QUI SE PASSAIT SANS ELLE, ET C'EST UN VRAI DEFAUT. Ces deux routes
    /// rendaient le message protobuf tel quel. System.Text.Json ne parle pas le
    /// JSON de protobuf : il serialise un enum genere en ENTIER et un Timestamp
    /// en <c>{ seconds, nanos }</c>. L'application recevait donc
    /// <c>"status": 4</c> la ou son lecteur attend
    /// <c>"DELIVERY_STATUS_SEARCHING_DRIVER"</c>, et un objet la ou il attend
    /// une date.
    ///
    /// LES CONSEQUENCES ETAIENT EN CHAINE, ET AUCUNE NE RESSEMBLAIT A SA CAUSE :
    /// toutes les pastilles de statut affichaient « -- » ; aucune course ne
    /// passait jamais en « terminee », puisque <c>isClosed</c> repondait
    /// toujours non ; le code de remise restait visible apres la livraison ;
    /// le bloc « Recherche d'un livreur » du point 23 ne pouvait jamais
    /// apparaitre ; et son compteur n'avait aucune date d'ou partir.
    ///
    /// LE PRINCIPE ETAIT DEJA ECRIT AILLEURS DANS CE DEPOT. La carte des
    /// positions du back-office porte ce commentaire : « ON NE REND PAS LE
    /// MESSAGE PROTOBUF TEL QUEL […] la console devrait alors connaitre la
    /// numerotation du .proto pour lire une reponse. » Il vaut mot pour mot
    /// pour l'application cliente ; ces deux routes-ci l'avaient manque.
    /// </summary>
    private static object Lisible(Delivery livraison) => new
    {
        id = livraison.Id,
        reference = livraison.Reference,
        status = Nommer(livraison.Status),
        recipientName = livraison.RecipientName,
        recipientPhone = livraison.RecipientPhone,
        pickup = livraison.Pickup is null ? null : new
        {
            landmark = livraison.Pickup.Landmark,
            latitude = livraison.Pickup.Point?.Latitude ?? 0,
            longitude = livraison.Pickup.Point?.Longitude ?? 0,
        },
        dropoff = livraison.Dropoff is null ? null : new
        {
            landmark = livraison.Dropoff.Landmark,
            latitude = livraison.Dropoff.Point?.Latitude ?? 0,
            longitude = livraison.Dropoff.Point?.Longitude ?? 0,
        },
        pricing = livraison.Pricing is null ? null : new
        {
            total = new { amount = livraison.Pricing.Total?.Amount ?? 0 },
        },
        driver = livraison.Driver is null ? null : new
        {
            displayName = livraison.Driver.DisplayName,
            phone = livraison.Driver.Phone,
            vehiclePlate = livraison.Driver.VehiclePlate,
        },
        deliveryOtp = livraison.DeliveryOtp,

        // ISO 8601, PAS { seconds, nanos }. C'est cette date qui fait avancer
        // le compteur « Commandee il y a … » de l'ecran de suivi.
        createdAt = livraison.CreatedAt?.ToDateTimeOffset(),
    };

    /// <summary>
    /// LES NOMS DU CONTRAT, PAS CEUX DU C# GENERE — meme convention que la
    /// carte des positions du back-office. Un switch plutot que la reflexion :
    /// si une valeur s'ajoute au contrat, celui-ci cesse de compiler, la ou la
    /// reflexion rendrait une chaine vide en silence.
    /// </summary>
    private static string Nommer(DeliveryStatus statut) => statut switch
    {
        DeliveryStatus.PendingPayment => "DELIVERY_STATUS_PENDING_PAYMENT",
        DeliveryStatus.PaymentFailed => "DELIVERY_STATUS_PAYMENT_FAILED",
        DeliveryStatus.Paid => "DELIVERY_STATUS_PAID",
        DeliveryStatus.SearchingDriver => "DELIVERY_STATUS_SEARCHING_DRIVER",
        DeliveryStatus.NoDriverFound => "DELIVERY_STATUS_NO_DRIVER_FOUND",
        DeliveryStatus.DriverAssigned => "DELIVERY_STATUS_DRIVER_ASSIGNED",
        DeliveryStatus.DriverAtPickup => "DELIVERY_STATUS_DRIVER_AT_PICKUP",
        DeliveryStatus.PickedUp => "DELIVERY_STATUS_PICKED_UP",
        DeliveryStatus.Delivered => "DELIVERY_STATUS_DELIVERED",
        DeliveryStatus.Cancelled => "DELIVERY_STATUS_CANCELLED",
        DeliveryStatus.Failed => "DELIVERY_STATUS_FAILED",
        _ => "DELIVERY_STATUS_UNSPECIFIED",
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
