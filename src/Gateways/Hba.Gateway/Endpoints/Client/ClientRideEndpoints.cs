using Hba.BuildingBlocks.Security;
using Hba.Contracts.Common.V1;
using Hba.Contracts.Payment.V1;
using Hba.Contracts.Ride.V1;
using ProtoRide = Hba.Contracts.Ride.V1.Ride;

namespace Hba.Gateway.Endpoints.Client;

/// <summary>
/// Surface REST du transport de personnes, côté passager.
/// </summary>
///
/// <remarks>
/// LE DEVIS N'A PAS DE ROUTE ICI, ET CE N'EST PAS UN OUBLI. <c>POST
/// /api/client/v1/quotes</c> fait déjà le travail : le passager y envoie
/// <c>nature: "course"</c> avec le type de véhicule voulu. Une seconde route
/// dupliquerait le chemin du prix, qui est exactement celui qu'on ne veut pas voir
/// diverger.
///
/// LA GRILLE, EN REVANCHE, N'EST PLUS LA MÊME. Jusqu'au 1er octobre 2026 une
/// course était chiffrée sur la grille du colis — même prix au kilomètre, même
/// plancher, même part. Depuis, la nature fait partie de la clé de la grille, et
/// le champ <c>nature</c> n'est donc PAS facultatif pour une course : sans lui,
/// Pricing rend un devis de colis et la création de la course échoue sur
/// <c>QUOTE_SERVICE_MISMATCH</c>.
///
/// C'EST ICI QUE L'IDENTITÉ DU CHAUFFEUR EST FILTRÉE. Le service rend un
/// <c>driver_id</c> dès l'affectation parce que le back-office et le chauffeur en
/// ont besoin ; le passager, lui, n'a jamais à voir cet identifiant. Il reçoit un
/// nom, un téléphone et un véhicule — de quoi reconnaître qui arrive et
/// l'appeler, rien de plus.
/// </remarks>
public static class ClientRideEndpoints
{
    public static IEndpointRouteBuilder MapClientRideEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app.MapGroup("/api/client/v1/rides").RequireAuthorization(HbaPolicies.Customer);

        group.MapPost("/", async (
            RequestRideDto body,
            HttpContext http,
            RideService.RideServiceClient rides,
            CancellationToken cancellationToken) =>
        {
            ArgumentNullException.ThrowIfNull(body);

            var reponse = await rides.RequestRideAsync(
                new RequestRideRequest
                {
                    // LA CLÉ EST CELLE DE L'EN-TÊTE, JAMAIS DU CORPS. Un en-tête
                    // Idempotency-Key est ce que relancent les bibliothèques HTTP
                    // à l'identique sur un nouvel essai ; un champ de corps, lui,
                    // peut être regénéré par l'application entre deux tentatives.
                    IdempotencyKey = http.Request.Headers["Idempotency-Key"].FirstOrDefault() ?? string.Empty,
                    QuoteId = body.QuoteId,
                    Pickup = Point(body.PickupLatitude, body.PickupLongitude),
                    Destination = Point(body.DestinationLatitude, body.DestinationLongitude),
                    VehicleType = Vehicule(body.VehicleType),

                    // LE NUMERO QUI PAIE, FACULTATIF. Vide, le service reprend
                    // celui du jeton ; de toute facon il ne fait que preremplir
                    // la page du fournisseur, ou le passager peut en saisir un
                    // autre.
                    PayerPhone = body.PayerPhone ?? string.Empty,
                },
                cancellationToken: cancellationToken);

            // LA REPONSE PORTE LA COURSE **ET** L'INTENTION DE PAIEMENT, comme
            // celle d'une livraison. Le prix est encaisse avant la recherche d'un
            // chauffeur (point 35) ; l'identifiant de l'intention est ce par quoi
            // le suivi apprendra si le passager a bien tape son code.
            var lisible = Lisible(reponse.Ride);

            return Results.Created(
                $"/api/client/v1/rides/{reponse.Ride.Id}",
                new
                {
                    ride = lisible,
                    payment = new
                    {
                        // VIDES QUAND L'OUVERTURE A ECHOUE. L'application doit le
                        // dire — « le paiement n'a pas pu s'ouvrir, reprenez votre
                        // course » — et non afficher un bouton qui ne mene nulle
                        // part.
                        intentId = reponse.PaymentIntentId,
                        redirectUrl = reponse.PaymentRedirectUrl,
                    },
                });
        });

        // LES COURSES EN COURS, AU PLURIEL (point 35). Un passager peut en avoir
        // plusieurs : une pour lui, une pour un proche. L'application choisit
        // celle qu'elle suit sur la carte et s'y tient — prendre la première de la
        // liste la ferait changer de course dès qu'une autre est commandée.
        group.MapGet("/active", async (
            RideService.RideServiceClient rides,
            CancellationToken cancellationToken) =>
        {
            var reponse = await rides.GetMyActiveRidesAsync(
                new GetMyActiveRidesRequest(),
                cancellationToken: cancellationToken);

            // UNE PROJECTION, PLUS UNE BOUCLE. Elle existait parce que chaque course
            // demandait un appel a Driver ; la course porte desormais son chauffeur.
            return Results.Ok(new { rides = reponse.Rides.Select(Lisible) });
        });

        group.MapGet("/{id}", async (
            string id,
            RideService.RideServiceClient rides,
            CancellationToken cancellationToken) =>
        {
            var course = await rides.GetRideAsync(
                new GetRideRequest { RideId = id },
                cancellationToken: cancellationToken);

            return Results.Ok(Lisible(course));
        });

        // L'HISTORIQUE PORTE LES CHAUFFEURS SANS COÛT, depuis que la course garde son
        // instantané : vingt courses terminées se lisaient autrefois en vingt appels à
        // Driver, et c'est pourquoi cette route n'affichait aucun nom.
        group.MapGet("/", async (
            RideService.RideServiceClient rides,
            CancellationToken cancellationToken,
            int pageSize = 20,
            int offset = 0) =>
        {
            var reponse = await rides.ListMyRidesAsync(
                new ListMyRidesRequest { PageSize = pageSize, Offset = offset },
                cancellationToken: cancellationToken);

            return Results.Ok(new
            {
                rides = reponse.Rides.Select(Lisible),
                total = reponse.Total,
            });
        });

        group.MapPost("/{id}/cancel", async (
            string id,
            CancelRideDto? body,
            RideService.RideServiceClient rides,
            CancellationToken cancellationToken) =>
        {
            var course = await rides.CancelRideAsync(
                new CancelRideRequest { RideId = id, Reason = body?.Reason ?? string.Empty },
                cancellationToken: cancellationToken);

            return Results.Ok(Lisible(course));
        });

        // LE POURBOIRE, APRES LA COURSE — point 42, tranche le 3 octobre 2026.
        //
        // C'EST UNE SECONDE PAGE DE PAIEMENT, et la route rend son adresse comme
        // celle de la course. L'application ouvre la meme webview ; rien de
        // nouveau de ce cote.
        //
        // LE BENEFICIAIRE N'EST PAS DANS LE CORPS. Payment le lit dans son grand
        // livre : laisser l'application nommer qui touche l'argent reviendrait a
        // permettre d'en envoyer a n'importe qui.
        //
        // AUCUN PLAFOND N'EST POSE ICI NON PLUS. Un plafond est une decision
        // commerciale que personne n'a prise, et une passerelle n'est pas
        // l'endroit ou l'inventer.
        group.MapPost("/{id}/tip", async (
            string id,
            TipDto body,
            HttpContext http,
            PaymentService.PaymentServiceClient payments,
            CancellationToken cancellationToken) =>
        {
            var intention = await payments.CreateTipIntentAsync(
                new CreateTipIntentRequest
                {
                    IdempotencyKey = http.Request.Headers["Idempotency-Key"].FirstOrDefault() ?? string.Empty,
                    ServiceType = Hba.Contracts.Common.V1.ServiceType.Ride,
                    ReferenceId = id,
                    PayerPhone = body.PayerPhone ?? string.Empty,
                    AmountXof = body.AmountXof,
                },
                cancellationToken: cancellationToken);

            return Results.Ok(new
            {
                payment = new
                {
                    // VIDES QUAND L'OUVERTURE A ECHOUE : l'application le dit,
                    // plutot que d'ouvrir une page blanche.
                    intentId = intention.Id,
                    redirectUrl = intention.RedirectUrl,
                },
            });
        });

        return app;
    }

    /// <summary>
    /// La course telle que l'application client la lit.
    /// </summary>
    ///
    /// <remarks>
    /// PLUS AUCUN APPEL A DRIVER ICI, ET C'EST LE CHANGEMENT DU 1er OCTOBRE 2026. Ce
    /// fichier interrogeait Driver A CHAQUE LECTURE pour afficher le nom du
    /// chauffeur : un ecran de suivi qui rafraichit toutes les cinq secondes faisait
    /// cinq appels par minute et par passager, et Driver indisponible une seconde
    /// effacait le nom. La course porte desormais l'instantane, pose une seule fois a
    /// l'affectation.
    ///
    /// LE FILTRAGE RESTE ICI. Le service rend l'identifiant du chauffeur parce que le
    /// back-office et le chauffeur lui-meme en ont besoin ; le passager, non. Il
    /// recoit de quoi reconnaitre et appeler, pas de quoi designer.
    /// </remarks>
    private static object Lisible(ProtoRide course) => new
    {
        id = course.Id,
        status = Nommer(course.Status),
        vehicleType = course.VehicleType.ToString(),
        pickup = new
        {
            latitude = course.Pickup?.Latitude ?? 0,
            longitude = course.Pickup?.Longitude ?? 0,
        },
        destination = new
        {
            latitude = course.Destination?.Latitude ?? 0,
            longitude = course.Destination?.Longitude ?? 0,
        },

        // LE PRIX ESTIMÉ EST LE PRIX DÛ (point 35). Les deux champs sortent quand
        // même : l'application affiche l'estimation avant la course et le montant
        // final après, et le jour où les deux divergeront, l'écran n'aura pas à
        // changer.
        estimatedPriceXof = course.EstimatedPrice?.Amount ?? 0,
        finalPriceXof = course.FinalPrice?.Amount,
        estimatedDistanceMeters = course.EstimatedDistanceMeters,
        estimatedDurationSeconds = course.EstimatedDurationSeconds,

        // AUCUN driverId ICI. Le passager reçoit de quoi reconnaître et appeler
        // son chauffeur, pas de quoi le désigner dans une autre requête.
        driver = course.Driver is null ? null : new
        {
            displayName = course.Driver.DisplayName,
            phone = course.Driver.Phone,
            vehicleType = course.Driver.VehicleType.ToString(),
            vehiclePlate = course.Driver.VehiclePlate,

            // « LA VOITURE BLANCHE » SE LIT À TRENTE MÈTRES, une plaque non. Le
            // libellé est composé par le domaine, qui seul sait quoi faire des trois
            // champs facultatifs dont il sort.
            vehicleLabel = course.Driver.VehicleLabel,
        },

        // ISO 8601, PAS { seconds, nanos } : ces dates font avancer les compteurs
        // de l'écran de suivi.
        requestedAt = course.RequestedAt?.ToDateTimeOffset(),
        driverAssignedAt = course.DriverAssignedAt?.ToDateTimeOffset(),
        driverArrivedAt = course.DriverArrivedAt?.ToDateTimeOffset(),
        startedAt = course.StartedAt?.ToDateTimeOffset(),
        completedAt = course.CompletedAt?.ToDateTimeOffset(),
        cancelledAt = course.CancelledAt?.ToDateTimeOffset(),
        cancellationReason = string.IsNullOrEmpty(course.CancellationReason) ? null : course.CancellationReason,

        // L'INTENTION DE PAIEMENT, POUR QUE LE SUPPORT PUISSE SUIVRE. Pas l'adresse
        // de paiement : elle expire, et un bouton « payer » menant sur une page
        // morte est pire que pas de bouton. Un passager qui a perdu sa page doit
        // reprendre une course — le devis de celle-ci est consomme.
        paymentIntentId = string.IsNullOrEmpty(course.PaymentIntentId) ? null : course.PaymentIntentId,

        // CE QUI SERAIT RETENU SI LE PASSAGER ANNULAIT MAINTENANT, calcule par le
        // service. Zero est une reponse — « annulation gratuite » — donc on envoie
        // le chiffre et non l'absence.
        retenueAnnulation = course.CancellationFeeDue?.Amount ?? 0,
    };

    /// <summary>
    /// L'état en texte stable, celui du contrat sans son préfixe.
    ///
    /// L'APPLICATION BRANCHE SUR CETTE CHAÎNE : la renommer change un affichage
    /// sans rien casser à la compilation. C'est la même forme que celle des
    /// livraisons, pour que les deux écrans de suivi se lisent pareil.
    /// </summary>
    private static string Nommer(Contracts.Ride.V1.RideStatus status) => status switch
    {
        Contracts.Ride.V1.RideStatus.Requested => "REQUESTED",
        Contracts.Ride.V1.RideStatus.SearchingDriver => "SEARCHING_DRIVER",
        Contracts.Ride.V1.RideStatus.DriverAssigned => "DRIVER_ASSIGNED",
        Contracts.Ride.V1.RideStatus.DriverArriving => "DRIVER_ARRIVING",
        Contracts.Ride.V1.RideStatus.DriverArrived => "DRIVER_ARRIVED",
        Contracts.Ride.V1.RideStatus.InProgress => "IN_PROGRESS",
        Contracts.Ride.V1.RideStatus.Completed => "COMPLETED",
        Contracts.Ride.V1.RideStatus.Cancelled => "CANCELLED",
        Contracts.Ride.V1.RideStatus.NoDriverAvailable => "NO_DRIVER_AVAILABLE",
        Contracts.Ride.V1.RideStatus.PaymentFailed => "PAYMENT_FAILED",
        _ => "UNSPECIFIED",
    };

    private static GeoPoint Point(double latitude, double longitude)
        => new() { Latitude = latitude, Longitude = longitude };

    /// <summary>
    /// LE CLIENT DIT CE QU'IL VEUT, LA PASSERELLE NE DÉCIDE PAS. Un nom inconnu
    /// devient UNSPECIFIED, et c'est le service qui tranche — comme pour les
    /// devis. Fourgon et vélo ne figurent pas : ils ne transportent personne, et
    /// le contrat de Ride ne les connaît pas.
    /// </summary>
    private static RideVehicleType Vehicule(string? nom) => nom?.Trim().ToUpperInvariant() switch
    {
        "MOTORCYCLE" or "MOTO" => RideVehicleType.Motorcycle,
        "CAR" or "VOITURE" => RideVehicleType.Car,
        "TRICYCLE" => RideVehicleType.Tricycle,
        _ => RideVehicleType.Unspecified,
    };
}

/// <summary>
/// Demande de course.
///
/// AUCUN MONTANT, SEULEMENT UN DEVIS. Un prix dans ce corps serait exactement ce
/// qu'un téléphone modifié enverrait ; un identifiant de devis ne se fabrique
/// pas.
/// </summary>
public sealed record RequestRideDto(
    string QuoteId,
    double PickupLatitude,
    double PickupLongitude,
    double DestinationLatitude,
    double DestinationLongitude,
    string? VehicleType = null,

    // LE NUMERO QUI PAIE, FACULTATIF POUR DE BON : vide, le service reprend
    // celui du jeton. Il ne fait que preremplir la page du fournisseur.
    string? PayerPhone = null);

public sealed record CancelRideDto(string? Reason = null);

/// <summary>
/// Le pourboire que le passager laisse apres sa course.
/// </summary>
///
/// <remarks>
/// LE MONTANT EST LE SEUL CHAMP OBLIGATOIRE. Le numero ne fait que preremplir la
/// page du fournisseur ; vide, le service reprend celui du jeton.
/// </remarks>
public sealed record TipDto(long AmountXof, string? PayerPhone = null);
