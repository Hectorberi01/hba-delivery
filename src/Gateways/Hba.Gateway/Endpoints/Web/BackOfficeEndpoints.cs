using Hba.BuildingBlocks.Security;
using Hba.Contracts.Common.V1;
using Hba.Contracts.Delivery.V1;
using Hba.Contracts.Dispatch.V1;
using Hba.Contracts.Driver.V1;

namespace Hba.Gateway.Endpoints.Web;

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
            string? pageToken = null,
            string? customerId = null) =>
        {
            var result = await deliveries.ListDeliveriesAsync(
                new ListDeliveriesRequest
                {
                    PageSize = pageSize,
                    PageToken = pageToken ?? string.Empty,
                    CustomerId = customerId ?? string.Empty,
                },
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

        // PROPOSER UNE COURSE A UN LIVREUR CHOISI.
        //
        // LE GROUPE EST CELUI DU BACK-OFFICE, MAIS LE SERVICE NE LAISSE
        // PASSER QU'ADMIN ET OPS. La politique de groupe ouvre a quatre
        // roles ; poser une offre n'en concerne que deux, et c'est Dispatch
        // qui tranche — « toute autorisation se verifie cote service ». Une
        // politique de plus ici ferait croire que la passerelle decide.
        group.MapPost("/deliveries/{id}/offer", async (
            string id,
            OfferDto body,
            DispatchService.DispatchServiceClient dispatch,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(body.DriverId))
            {
                return Results.BadRequest(new { code = "MISSING_DRIVER_ID" });
            }

            var result = await dispatch.OfferToDriverAsync(
                new OfferToDriverRequest
                {
                    DeliveryId = id,
                    DriverId = body.DriverId,
                    Reason = body.Reason ?? string.Empty,
                },
                cancellationToken: cancellationToken);

            // UN REFUS METIER N'EST PAS UNE ERREUR HTTP. « Ce livreur vient
            // de partir en course » est une reponse, pas une panne : la
            // console doit l'afficher telle quelle, et un 4xx la ferait
            // passer par le chemin des erreurs, ou le code se perd.
            return Results.Ok(new
            {
                sent = result.Sent,
                rejectionCode = result.RejectionCode,
                offer = result.Offer is null
                    ? null
                    : new
                    {
                        id = result.Offer.Id,
                        driverId = result.Offer.DriverId,
                        expiresAt = result.Offer.ExpiresAt?.ToDateTimeOffset(),
                        distanceToPickupMeters = result.Offer.DistanceToPickupMeters,
                        driverEarningXof = result.Offer.DriverEarningXof,
                    },
            });
        });

        // LECTURE OUVERTE AU BACK-OFFICE, ECRITURE RESERVEE A L'ADMIN. Le
        // support a besoin de retrouver un livreur pour repondre a un client ;
        // valider un KYC ou suspendre quelqu'un, non. Les deux groupes portent
        // donc deux politiques differentes, et chaque handler revérifie.
        var annuaire = app.MapGroup("/api/admin/v1/drivers").RequireAuthorization(HbaPolicies.BackOffice);

        annuaire.MapGet("", async (
            DriverService.DriverServiceClient driverService,
            CancellationToken cancellationToken,
            string? query = null,
            int pageSize = 50,
            int offset = 0) =>
        {
            var result = await driverService.ListDriversAsync(
                new ListDriversRequest
                {
                    Query = query ?? string.Empty,
                    PageSize = pageSize,
                    Offset = offset,
                },
                cancellationToken: cancellationToken);

            return Results.Ok(result);
        });

        // LA CARTE EST UNE LECTURE, DONC BACK-OFFICE. Le service revérifie —
        // savoir où se tient une personne en ce moment est la donnée la plus
        // sensible après les pièces d'identité.
        annuaire.MapGet("/positions", async (
            DriverService.DriverServiceClient driverService,
            CancellationToken cancellationToken,
            int limit = 0) =>
        {
            var result = await driverService.ListDriverPositionsAsync(
                new ListDriverPositionsRequest { Limit = limit },
                cancellationToken: cancellationToken);

            // ON NE REND PAS LE MESSAGE PROTOBUF TEL QUEL, contrairement aux
            // routes voisines. System.Text.Json sérialise un enum généré en
            // ENTIER et un Timestamp en { seconds, nanos } : la console
            // devrait alors connaître la numérotation du .proto pour lire une
            // réponse. Les noms du contrat et une date ISO se lisent seuls.
            return Results.Ok(new
            {
                freshnessSeconds = result.FreshnessSeconds,
                positions = result.Positions.Select(position => new
                {
                    driverId = position.DriverId,
                    displayName = position.DisplayName,
                    latitude = position.Latitude,
                    longitude = position.Longitude,
                    seenAt = position.SeenAt?.ToDateTimeOffset(),
                    operationalStatus = Nommer(position.OperationalStatus),
                    vehicleType = Nommer(position.VehicleType),
                }),
            });
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

    // LES NOMS DU CONTRAT, PAS CEUX DU C# GENERE. « OPERATIONAL_STATUS_AVAILABLE »
    // est ce qu'écrit le .proto ; le C# généré dit « Available ». Le premier
    // est la source de vérité, et c'est déjà la convention que suit
    // lireStatut() côté console.
    //
    // UN SWITCH PLUTOT QUE LA REFLEXION SUR LE DESCRIPTEUR : si une valeur
    // s'ajoute au contrat, celui-ci cesse de compiler, là où la réflexion
    // aurait rendu une chaîne vide en silence.
    private static string Nommer(OperationalStatus statut) => statut switch
    {
        OperationalStatus.Offline => "OPERATIONAL_STATUS_OFFLINE",
        OperationalStatus.Available => "OPERATIONAL_STATUS_AVAILABLE",
        OperationalStatus.Reserved => "OPERATIONAL_STATUS_RESERVED",
        OperationalStatus.OnMission => "OPERATIONAL_STATUS_ON_MISSION",
        _ => "OPERATIONAL_STATUS_UNSPECIFIED",
    };

    private static string Nommer(VehicleType type) => type switch
    {
        VehicleType.Motorcycle => "VEHICLE_TYPE_MOTORCYCLE",
        VehicleType.Car => "VEHICLE_TYPE_CAR",
        VehicleType.Van => "VEHICLE_TYPE_VAN",
        _ => "VEHICLE_TYPE_UNSPECIFIED",
    };
}

public sealed record CloseDto(bool Failed, string Reason);

public sealed record ReassignDto(string Reason, string? TargetDriverId);

public sealed record OfferDto(string DriverId, string? Reason);

public sealed record KycDto(bool Approved, string? Reason);

public sealed record SuspendDto(string Reason);
