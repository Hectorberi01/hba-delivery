using Hba.BuildingBlocks.Security;
using Hba.Contracts.Common.V1;
using Hba.Contracts.Delivery.V1;
using Hba.Contracts.Dispatch.V1;
using Hba.Contracts.Driver.V1;
using Hba.Contracts.Location.V1;

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

        // UNE COURSE SEULE, ET ELLE MANQUAIT. L'écran des commandes affichait son
        // détail en choisissant une ligne dans la page déjà chargée : ouvrir une
        // course par son lien, la rafraîchir ou la mettre en favori était donc
        // impossible — il n'y avait rien à ouvrir.
        //
        // AUCUN FILTRE DE PÉRIMÈTRE ICI, ET CE N'EST PAS UN OUBLI : la politique
        // du groupe exige déjà le back-office, et le service revérifie l'appelant
        // (DeliveryAccess, ADR 0007). Une course inconnue sort en NOT_FOUND, que
        // RpcExceptionMiddleware traduit en 404.
        group.MapGet("/deliveries/{id}", async (
            string id,
            DeliveryService.DeliveryServiceClient deliveries,
            CancellationToken cancellationToken) =>
        {
            var course = await deliveries.GetDeliveryAsync(
                new GetDeliveryRequest { DeliveryId = id },
                cancellationToken: cancellationToken);

            return Results.Ok(course);
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
                    ReferenceId = id,

                    // CETTE ROUTE PORTE SUR UNE LIVRAISON, et le dire est devenu
                    // obligatoire : le moteur cherche aussi pour des courses de
                    // personnes, et deux missions de natures differentes peuvent
                    // porter le meme identifiant.
                    //
                    // L'APPEL REPOND TOUJOURS UNIMPLEMENTED, et ce n'est pas lie a
                    // ce changement : reaffecter une course deja acceptee touche a
                    // la politique d'annulation — point 3, non tranche.
                    ServiceType = Hba.Contracts.Common.V1.ServiceType.Delivery,

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
                    ReferenceId = id,

                    // CETTE ROUTE PORTE SUR UNE LIVRAISON, et le dire est devenu
                    // obligatoire : depuis le 1er octobre 2026 le moteur cherche
                    // aussi pour des courses de personnes, et il refuse une mission
                    // sans nature plutot que de supposer le colis. L'equivalent
                    // pour les courses s'ajoutera avec son propre groupe de routes.
                    ServiceType = Hba.Contracts.Common.V1.ServiceType.Delivery,

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

        // UN LIVREUR SEUL, ET IL MANQUAIT POUR LA MEME RAISON QUE LA COURSE : la
        // fiche se choisissait dans l'annuaire deja charge, donc elle n'avait pas de
        // lien a elle. Une fiche de livreur se partage pourtant — « regarde ce
        // dossier » est exactement ce qu'on dit au support.
        //
        // LECTURE, DONC BACK-OFFICE. Le groupe porte deja la politique, et le service
        // revérifie l'appelant.
        annuaire.MapGet("/{id}", async (
            string id,
            DriverService.DriverServiceClient driverService,
            CancellationToken cancellationToken) =>
        {
            var livreur = await driverService.GetDriverAsync(
                new GetDriverRequest { DriverId = id },
                cancellationToken: cancellationToken);

            return Results.Ok(livreur);
        });

        // LA CARTE EST UNE LECTURE, DONC BACK-OFFICE. Les DEUX services revérifient —
        // savoir où se tient une personne en ce moment est la donnée la plus sensible
        // après les pièces d'identité.
        //
        // DEUX APPELS DEPUIS LE 1er OCTOBRE 2026, ET C'EST TOUT L'OBJET DE LA PHASE 9.
        // Location rend les points et leur fraîcheur, sans savoir à qui ils
        // appartiennent ; Driver nomme les livreurs, sans savoir où ils sont. Cette
        // route est le seul endroit du dépôt où les deux moitiés se recollent pour un
        // écran humain — et c'est précisément ce découpage qui permet à Location de
        // tenir des positions sans tenir un fichier de personnes.
        //
        // ON PART DES POSITIONS, PAS DE L'ANNUAIRE, et l'ordre n'est pas interchangeable.
        // Partir de l'annuaire demanderait la position de chaque livreur un par un ;
        // partir des positions ne ramène que ceux qui ont donné signe de vie, c'est-à-dire
        // exactement ce que la carte doit montrer.
        annuaire.MapGet("/positions", async (
            LocationService.LocationServiceClient locations,
            DriverService.DriverServiceClient driverService,
            CancellationToken cancellationToken,
            int limit = 0) =>
        {
            var points = await locations.ListFreshPositionsAsync(
                new ListFreshPositionsRequest { Limit = limit },
                cancellationToken: cancellationToken);

            if (points.Positions.Count == 0)
            {
                // AUCUN POINT, AUCUN APPEL A DRIVER. La fenêtre est rendue quand même :
                // « aucun livreur » sans « vu dans les 120 dernières secondes » se lit
                // comme un effectif nul alors que c'est une fenêtre glissante.
                return Results.Ok(new
                {
                    freshnessSeconds = points.FreshnessSeconds,
                    positions = Array.Empty<object>(),
                });
            }

            var profils = new GetDriverSummariesRequest();
            profils.DriverIds.AddRange(points.Positions.Select(p => p.DriverId));

            var annuaires = await driverService.GetDriverSummariesAsync(
                profils,
                cancellationToken: cancellationToken);

            var parId = annuaires.Drivers.ToDictionary(d => d.DriverId, StringComparer.Ordinal);

            // ON NE REND PAS LE MESSAGE PROTOBUF TEL QUEL, contrairement aux routes
            // voisines. System.Text.Json sérialise un enum généré en ENTIER et un
            // Timestamp en { seconds, nanos } : la console devrait alors connaître la
            // numérotation du .proto pour lire une réponse. Les noms du contrat et une
            // date ISO se lisent seuls.
            //
            // UN POINT SANS PROFIL EST IGNORÉ, PAS INVENTÉ. Le cas ne devrait pas se
            // produire — le profil naît avant le premier battement — mais s'il arrive, un
            // marqueur « livreur inconnu » sur la carte poserait à ops une question à
            // laquelle personne ne peut répondre.
            return Results.Ok(new
            {
                freshnessSeconds = points.FreshnessSeconds,
                positions = points.Positions
                    .Where(position => parId.ContainsKey(position.DriverId))
                    .Select(position =>
                    {
                        var profil = parId[position.DriverId];

                        return new
                        {
                            driverId = position.DriverId,
                            displayName = profil.DisplayName,
                            latitude = position.Point?.Latitude ?? 0,
                            longitude = position.Point?.Longitude ?? 0,
                            seenAt = position.SeenAt?.ToDateTimeOffset(),
                            operationalStatus = Nommer(profil.OperationalStatus),
                            vehicleType = Nommer(profil.VehicleType),

                            // LES TROIS NOUVEAUX CHAMPS DE LA PHASE 9. Cap et vitesse
                            // existaient au contrat de Driver depuis le début et étaient
                            // jetés à la frontière gRPC : aucune carte n'a jamais pu
                            // orienter une flèche. La précision n'existait nulle part.
                            //
                            // NULS QUAND LE TÉLÉPHONE N'A RIEN DIT, et non zéro : zéro
                            // degré veut dire « plein nord », et zéro mètre par seconde
                            // « à l'arrêt ». La console doit pouvoir distinguer les deux.
                            headingDegrees = position.HasHeadingDegrees
                                ? position.HeadingDegrees
                                : (double?)null,
                            speedMps = position.HasSpeedMps ? position.SpeedMps : (double?)null,
                            accuracyMeters = position.HasAccuracyMeters
                                ? position.AccuracyMeters
                                : (double?)null,
                        };
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

        // LEVER LA SUSPENSION, CE QUI N'EXISTAIT NULLE PART. Le domaine promettait
        // « seul ops la leve » et aucune porte ne le permettait : un livreur
        // suspendu par erreur etait exclu a vie.
        drivers.MapPost("/{driverId}/reinstate", async (
            string driverId,
            SuspendDto body,
            DriverService.DriverServiceClient driverService,
            CancellationToken cancellationToken) =>
        {
            var result = await driverService.ReinstateDriverAsync(
                new ReinstateDriverRequest { DriverId = driverId, Reason = body.Reason },
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
        VehicleType.Bicycle => "VEHICLE_TYPE_BICYCLE",
        VehicleType.Tricycle => "VEHICLE_TYPE_TRICYCLE",
        _ => "VEHICLE_TYPE_UNSPECIFIED",
    };
}

public sealed record CloseDto(bool Failed, string Reason);

public sealed record ReassignDto(string Reason, string? TargetDriverId);

public sealed record OfferDto(string DriverId, string? Reason);

public sealed record KycDto(bool Approved, string? Reason);

public sealed record SuspendDto(string Reason);
