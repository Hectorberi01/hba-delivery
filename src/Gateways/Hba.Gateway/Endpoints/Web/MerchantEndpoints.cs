using Hba.BuildingBlocks.Security;
using Hba.Contracts.Common.V1;
using Hba.Contracts.Delivery.V1;
using Hba.Contracts.Pricing.V1;

namespace Hba.Gateway.Endpoints.Web;

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

        // LE DEVIS, PUIS LA COURSE. Deux appels et non un : le prix est ferme
        // avant que le commercant valide, et c'est ce devis-la que Delivery
        // consomme. Sans cette etape, le commercant decouvrirait le montant
        // apres le debit de son compte prepaye.
        group.MapPost("/quotes", async (
            DevisCommercantDto body,
            PricingService.PricingServiceClient pricing,
            CancellationToken cancellationToken) =>
        {
            var devis = await pricing.GetQuoteAsync(
                new GetQuoteRequest
                {
                    Pickup = new GeoPoint { Latitude = body.PickupLatitude, Longitude = body.PickupLongitude },
                    Dropoff = new GeoPoint { Latitude = body.DropoffLatitude, Longitude = body.DropoffLongitude },
                    VehicleType = Vehicule(body.VehicleType),
                    Source = Source.MerchantPortal,

                    // UN COMMERCANT FAIT LIVRER UN COLIS, PAS UN PASSAGER. La
                    // nature est posee ici et non laissee au navigateur : le
                    // portail n'a aucun ecran de transport de personnes, et un
                    // devis de course consomme a la creation echouerait sur
                    // QUOTE_SERVICE_MISMATCH.
                    ServiceType = ServiceType.Delivery,
                    PackageWeightGrams = body.PackageWeightGrams,
                },
                cancellationToken: cancellationToken);

            return Results.Ok(new
            {
                quoteId = devis.Id,
                totalXof = devis.Total?.Amount ?? 0,
                distanceMeters = devis.DistanceMeters,
                durationSeconds = devis.DurationSeconds,
                expiresAt = devis.ExpiresAt?.ToDateTimeOffset(),
            });
        });

        // LA CREATION. Le commercant n'est PAS nomme dans le corps : Delivery le
        // lit dans le claim merchant_id du jeton (ResolveOrderer), et le compte
        // prepaye debite est celui-la. Accepter un merchantId du navigateur
        // reviendrait a laisser commander sur le compte d'un autre.
        group.MapPost("/deliveries", async (
            CourseCommercantDto body,
            HttpContext http,
            DeliveryService.DeliveryServiceClient deliveries,
            CancellationToken cancellationToken) =>
        {
            var reponse = await deliveries.CreateDeliveryAsync(
                new CreateDeliveryRequest
                {
                    IdempotencyKey = http.Request.Headers["Idempotency-Key"].FirstOrDefault() ?? string.Empty,
                    QuoteId = body.QuoteId,
                    Source = Source.MerchantPortal,
                    PickupPointId = body.PickupPointId ?? string.Empty,
                    Pickup = VersLieu(body.Pickup),
                    Dropoff = VersLieu(body.Dropoff),
                    RecipientName = body.RecipientName,
                    RecipientPhone = body.RecipientPhone,
                    PackageDescription = body.PackageDescription ?? string.Empty,
                    PackageWeightGrams = body.PackageWeightGrams,
                },
                cancellationToken: cancellationToken);

            // PAS D'INTENTION DE PAIEMENT ICI, ET C'EST NORMAL : le compte
            // prepaye du commercant a deja ete debite par Delivery (point 1,
            // tranche le 29 septembre 2026). Un solde insuffisant se voit comme
            // un refus de la creation, pas comme une course a payer.
            return Results.Created(
                $"/api/merchant/v1/deliveries/{reponse.Delivery.Id}",
                reponse.Delivery);
        });

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

    private static Location VersLieu(LieuCommercantDto dto) => new()
    {
        Point = new GeoPoint { Latitude = dto.Latitude, Longitude = dto.Longitude },
        Landmark = dto.Landmark,
        Phone = dto.Phone,
        ContactName = dto.ContactName,
        Notes = dto.Notes ?? string.Empty,
    };

    /// <summary>
    /// UN NOM INCONNU DEVIENT UNSPECIFIED, QUE PRICING TRADUIT EN MOTO. Le repli
    /// vit dans le service : deux replis, un ici et un la-bas, finiraient par
    /// differer sans que rien ne le signale.
    /// </summary>
    private static VehicleType Vehicule(string? nom) => nom?.Trim().ToLowerInvariant() switch
    {
        "moto" or "motorcycle" => VehicleType.Motorcycle,
        "voiture" or "car" => VehicleType.Car,
        "camionnette" or "van" => VehicleType.Van,
        "velo" or "vélo" or "bicycle" => VehicleType.Bicycle,
        "tricycle" => VehicleType.Tricycle,
        _ => VehicleType.Unspecified,
    };
}

public sealed record MerchantCancelDto(string Reason);

public sealed record DevisCommercantDto(
    double PickupLatitude,
    double PickupLongitude,
    double DropoffLatitude,
    double DropoffLongitude,
    int PackageWeightGrams,

    // « moto », « voiture », « camionnette », « velo », « tricycle ». Absent,
    // il vaut la moto — et c'est Pricing qui applique ce repli, pas cette
    // passerelle.
    string? VehicleType = null);

public sealed record LieuCommercantDto(
    double Latitude,
    double Longitude,
    string Landmark,
    string Phone,
    string ContactName,
    string? Notes);

public sealed record CourseCommercantDto(
    string QuoteId,
    LieuCommercantDto Pickup,
    LieuCommercantDto Dropoff,
    string RecipientName,
    string RecipientPhone,
    string? PickupPointId,
    string? PackageDescription,
    int PackageWeightGrams);
