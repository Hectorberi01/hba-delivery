using Hba.BuildingBlocks.Security;
using Hba.Contracts.Ride.V1;
using ProtoRide = Hba.Contracts.Ride.V1.Ride;

namespace Hba.Gateway.Endpoints.Driver;

/// <summary>
/// Surface REST du transport de personnes, côté chauffeur.
/// </summary>
///
/// <remarks>
/// AUCUNE ROUTE N'ACCEPTE D'IDENTIFIANT DE CHAUFFEUR. Il est lu dans le jeton par
/// le service lui-même (ADR 0007) : un champ dans l'URL ou le corps permettrait à
/// un chauffeur de démarrer la course d'un autre.
///
/// LE CHAUFFEUR VOIT LE PASSAGER, CONTRAIREMENT À L'INVERSE AVANT AFFECTATION.
/// Ici c'est <c>customerId</c> qui est caché : le chauffeur n'a pas à désigner un
/// client dans une autre requête. Il a besoin des deux points et de l'état, pas
/// d'un identifiant de compte.
/// </remarks>
public static class DriverRideEndpoints
{
    public static IEndpointRouteBuilder MapDriverRideEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app.MapGroup("/api/driver/v1/rides").RequireAuthorization(HbaPolicies.Driver);

        // CE QUE L'APPLICATION INTERROGE AU RÉVEIL, après une coupure réseau ou un
        // redémarrage du téléphone. Sans cette route, un chauffeur rouvrant son
        // application perdrait la mission qu'il est en train d'exécuter.
        //
        // UNE SEULE COURSE, AU SINGULIER : il ne conduit qu'un véhicule. C'est la
        // différence avec le côté passager, qui peut en avoir plusieurs.
        group.MapGet("/active", async (
            RideService.RideServiceClient rides,
            CancellationToken cancellationToken) =>
        {
            var reponse = await rides.GetMyDriverRideAsync(
                new GetMyDriverRideRequest(),
                cancellationToken: cancellationToken);

            // 200 AVEC null, PAS 404. « Aucune course » est l'état ordinaire d'un
            // chauffeur en ligne : en faire une erreur obligerait l'application à
            // traiter un échec pour afficher un écran d'attente.
            return Results.Ok(new { ride = reponse.Present ? Lisible(reponse.Ride) : null });
        });

        group.MapPost("/{id}/on-the-way", async (
            string id,
            RideService.RideServiceClient rides,
            CancellationToken cancellationToken) =>
        {
            var course = await rides.DeclareOnTheWayAsync(
                new DeclareOnTheWayRequest { RideId = id },
                cancellationToken: cancellationToken);

            return Results.Ok(Lisible(course));
        });

        group.MapPost("/{id}/arrived", async (
            string id,
            RideService.RideServiceClient rides,
            CancellationToken cancellationToken) =>
        {
            var course = await rides.DeclareArrivedAsync(
                new DeclareArrivedRequest { RideId = id },
                cancellationToken: cancellationToken);

            return Results.Ok(Lisible(course));
        });

        group.MapPost("/{id}/start", async (
            string id,
            RideService.RideServiceClient rides,
            CancellationToken cancellationToken) =>
        {
            var course = await rides.StartRideAsync(
                new StartRideRequest { RideId = id },
                cancellationToken: cancellationToken);

            return Results.Ok(Lisible(course));
        });

        // AUCUN MONTANT DANS CET APPEL. Le prix est ferme à la commande
        // (point 35) : le laisser envoyer un total ferait du chauffeur celui qui
        // décide de ce qui est dû.
        group.MapPost("/{id}/complete", async (
            string id,
            RideService.RideServiceClient rides,
            CancellationToken cancellationToken) =>
        {
            var course = await rides.CompleteRideAsync(
                new CompleteRideRequest { RideId = id },
                cancellationToken: cancellationToken);

            return Results.Ok(Lisible(course));
        });

        group.MapPost("/{id}/cancel", async (
            string id,
            DriverCancelRideDto? body,
            RideService.RideServiceClient rides,
            CancellationToken cancellationToken) =>
        {
            var course = await rides.CancelRideAsync(
                new CancelRideRequest { RideId = id, Reason = body?.Reason ?? string.Empty },
                cancellationToken: cancellationToken);

            return Results.Ok(Lisible(course));
        });

        return app;
    }

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

        // CE QUE LE CHAUFFEUR TOUCHERA N'EST PAS CE MONTANT, et il faut le savoir
        // avant d'en faire un écran. C'est le prix payé par le passager ; la part
        // du chauffeur est calculée par Pricing puis par Billing, et Ride ne la
        // porte pas. L'afficher comme « votre gain » serait un mensonge.
        estimatedPriceXof = course.EstimatedPrice?.Amount ?? 0,
        finalPriceXof = course.FinalPrice?.Amount,
        estimatedDistanceMeters = course.EstimatedDistanceMeters,
        estimatedDurationSeconds = course.EstimatedDurationSeconds,

        requestedAt = course.RequestedAt?.ToDateTimeOffset(),
        driverAssignedAt = course.DriverAssignedAt?.ToDateTimeOffset(),
        driverArrivedAt = course.DriverArrivedAt?.ToDateTimeOffset(),
        startedAt = course.StartedAt?.ToDateTimeOffset(),
        completedAt = course.CompletedAt?.ToDateTimeOffset(),
        cancelledAt = course.CancelledAt?.ToDateTimeOffset(),
        cancellationReason = string.IsNullOrEmpty(course.CancellationReason) ? null : course.CancellationReason,
    };

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

        // UN CHAUFFEUR NE VOIT JAMAIS CET ETAT, puisqu'une course impayee n'est
        // jamais dispatchee. Il est traduit quand meme : la table doit couvrir le
        // contrat, sinon un etat ajoute ressortirait en « UNSPECIFIED » sans que
        // rien ne le signale.
        Contracts.Ride.V1.RideStatus.PaymentFailed => "PAYMENT_FAILED",
        _ => "UNSPECIFIED",
    };
}

public sealed record DriverCancelRideDto(string? Reason = null);
