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
                    VehicleType = Vehicule(body.VehicleType),
                    Source = Source.ClientApp,
                    PackageWeightGrams = body.PackageWeightGrams,
                    ServiceType = Nature(body.ServiceType),
                },
                cancellationToken: cancellationToken);

            return Results.Ok(new
            {
                quoteId = quote.Id,
                nature = quote.ServiceType.ToString(),
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
                // LE NUMERO QUI PAIE, FACULTATIF : vide, le service reprend
                // celui du jeton. Il ne fait que preremplir la page.
                PayerPhone = body.PayerPhone ?? string.Empty,
            };

            var response = await delivery.CreateDeliveryAsync(request, cancellationToken: cancellationToken);

            return Results.Created($"/api/client/v1/deliveries/{response.Delivery.Id}",
                new
                {
                    delivery = Lisible(response.Delivery),
                    payment = new
                    {
                        // VIDES QUAND L'OUVERTURE A ECHOUE : l'application le dit,
                        // plutot que d'afficher un bouton qui ne mene nulle part.
                        intentId = response.PaymentIntentId,
                        redirectUrl = response.PaymentRedirectUrl,
                    },
                });
        });

        // « ?itineraire=1 » DEMANDE LE TRACE EN PLUS DE LA DUREE.
        //
        // EN PARAMETRE ET NON PAR DEFAUT, parce qu'il coute : quelques
        // kilo-octets par lecture, pour une geometrie que seul l'ecran de suivi
        // dessine. Le bandeau de l'accueil relit la meme route toutes les trente
        // secondes et n'affiche que quatre mots ; a Cotonou, le client paie ces
        // octets au megaoctet.
        //
        // EN FRANCAIS COMME LE RESTE DES ROUTES CLIENTES, et traduit vers le nom
        // du contrat juste en dessous.
        group.MapGet("/deliveries/{id}", async (
            string id,
            DeliveryService.DeliveryServiceClient delivery,
            CancellationToken cancellationToken,
            bool itineraire = false) =>
        {
            var result = await delivery.GetDeliveryAsync(
                new GetDeliveryRequest { DeliveryId = id, IncludeRoute = itineraire },
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
            baseFareXof = livraison.Pricing.BaseFare?.Amount ?? 0,
            distanceFareXof = livraison.Pricing.DistanceFare?.Amount ?? 0,
            surgeFareXof = livraison.Pricing.SurgeFare?.Amount ?? 0,
            distanceMeters = livraison.Pricing.DistanceMeters,
            durationSeconds = livraison.Pricing.DurationSeconds,
        },
        driver = livraison.Driver is null ? null : new
        {
            displayName = livraison.Driver.DisplayName,
            phone = livraison.Driver.Phone,
            vehicleType = livraison.Driver.VehicleType.ToString(),
            vehiclePlate = livraison.Driver.VehiclePlate,
        },
        deliveryOtp = livraison.DeliveryOtp,
        createdAt = livraison.CreatedAt?.ToDateTimeOffset(),
        paidAt = livraison.PaidAt?.ToDateTimeOffset(),
        completedAt = livraison.CompletedAt?.ToDateTimeOffset(),
        closureReason = string.IsNullOrEmpty(livraison.ClosureReason) ? null : livraison.ClosureReason,
        refundedAt = livraison.RefundedAt?.ToDateTimeOffset(),
        refundPartial = livraison.RefundPartial,
        retenueAnnulation = livraison.CancellationFeeDue?.Amount ?? 0,

        // L'ITINERAIRE ACTIF, OU NUL QUAND ON NE SAIT PAS.
        //
        // NUL N'EST PAS « IL EST ARRIVE » : aucun livreur affecte, aucune
        // position remontee, Routing injoignable, mission close. L'application
        // doit traiter ce cas — elle retombe sur la distance a vol d'oiseau,
        // qu'elle calcule seule.
        //
        // « DU REPLI » VOYAGE AVEC LA DUREE ET N'EST PAS OPTIONNEL. Une mesure a
        // vol d'oiseau est sous-estimee d'environ un tiers en ville : sans ce
        // drapeau, l'application afficherait une promesse que la course ne
        // tiendra pas. Point 43.
        itineraire = livraison.ActiveRoute is null ? null : new
        {
            distanceMetres = livraison.ActiveRoute.DistanceMeters,
            dureeSecondes = livraison.ActiveRoute.DurationSeconds,
            calculeA = livraison.ActiveRoute.CalculatedAt?.ToDateTimeOffset(),
            duRepli = livraison.ActiveRoute.FromFallback,

            // VIDE QUAND LE TRACE N'A PAS ETE DEMANDE, et vide aussi quand c'est
            // le repli qui a repondu : une mesure a vol d'oiseau ne dessine
            // aucune rue.
            trace = livraison.ActiveRoute.EncodedPolyline,
        },
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
    /// <summary>
    /// Le vehicule demande, ecrit en toutes lettres par l'application.
    ///
    /// LA PASSERELLE NE TRANCHE PAS L'INCONNU. Un nom qu'elle ne reconnait pas
    /// devient UNSPECIFIED et part tel quel : Pricing decide alors, et il le
    /// fait au meme endroit que pour tous les autres appelants. Traduire ici
    /// « voitur » en moto ferait payer un prix qui n'est pas celui demande,
    /// sans que rien ne le signale.
    /// </summary>
    /// <summary>
    /// La nature, ecrite en toutes lettres par l'application.
    ///
    /// UN NOM INCONNU DEVIENT UNSPECIFIED, QUE PRICING TRADUIT EN COLIS. Le repli
    /// vit dans le service, pas ici : c'est la meme regle que pour le vehicule.
    /// </summary>
    private static ServiceType Nature(string? nom) => nom?.Trim().ToLowerInvariant() switch
    {
        "colis" or "livraison" or "delivery" => ServiceType.Delivery,
        "course" or "ride" => ServiceType.Ride,
        _ => ServiceType.Unspecified,
    };

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

public sealed record QuoteRequestDto(
    double PickupLatitude,
    double PickupLongitude,
    double DropoffLatitude,
    double DropoffLongitude,
    int PackageWeightGrams,

    // LE VEHICULE SOUHAITE : « moto », « voiture », « velo », « tricycle ».
    //
    // EN COMMENTAIRE SIMPLE, COMME PARTOUT DANS CE DEPOT : aucun record
    // positionnel n'y documente ses parametres en « /// ». Ce qui est explique
    // ci-dessous l'est donc au meme endroit et de la meme facon qu'ailleurs.
    //
    // FACULTATIF, ET ABSENT IL VAUT LA MOTO. Ce n'est pas un defaut arbitraire :
    // a Cotonou la course se fait a moto, le zemidjan est le mode par defaut du
    // marche, et c'est la grille la moins chere — un client qui ne precise rien
    // ne se voit donc jamais facturer le tarif d'une camionnette. Pricing tient
    // deja ce raisonnement, mot pour mot, dans son ToDomain.
    //
    // AVANT LE 1er OCTOBRE 2026, CE CHAMP N'EXISTAIT PAS et la passerelle
    // ecrivait « Motorcycle » EN DUR. L'application cliente ne pouvait donc pas
    // demander le prix d'une voiture, meme si la grille existait — et une fois
    // les cinq types de vehicule ouverts, c'etait le seul obstacle restant entre
    // un client et un devis de tricycle.
    string? VehicleType = null,

    // LA NATURE DE LA PRESTATION : « colis » ou « course ».
    //
    // FACULTATIF, ET ABSENT IL VAUT LE COLIS — mais c'est PRICING qui applique ce
    // repli, pas cette passerelle. La raison tient a l'histoire du champ : avant le
    // 1er octobre 2026 une course de personnes etait chiffree sur la grille du
    // colis, et toutes les applications deja installees demandent des devis sans
    // nommer la nature. Les refuser aurait arrete les livraisons le jour de la mise
    // en service.
    //
    // CETTE ROUTE SERT LES DEUX METIERS, et c'est voulu : le prix a UN chemin. Un
    // passager demande son devis ici, avec « course », puis POST /rides. S'il oublie
    // le champ, il obtient un devis de colis et la creation de la course echouera sur
    // QUOTE_SERVICE_MISMATCH — un refus explicite, et non une course facturee au
    // tarif d'un paquet.
    string? ServiceType = null);

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
    int PackageWeightGrams,

    // LE NUMERO QUI PAIE, FACULTATIF POUR DE BON : vide, le service reprend
    // celui du jeton. Il ne fait que preremplir la page du fournisseur.
    string? PayerPhone = null);

public sealed record CancelDto(string Reason);
