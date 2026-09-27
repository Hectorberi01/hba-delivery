using Hba.BuildingBlocks.Security;
using Hba.Contracts.Common.V1;
using Hba.Contracts.Delivery.V1;
using Hba.Contracts.Dispatch.V1;
using Hba.Contracts.Driver.V1;
using Hba.Contracts.Payment.V1;

namespace Hba.Gateway.Endpoints.Driver;

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

        // CE QUE L'APP N'AVAIT AUCUN MOYEN D'APPRENDRE.
        //
        // Le livreur ne peut pas travailler tant que ses pieces ne sont pas
        // validees, et l'app doit vivre cet etat plutot que de le traiter
        // comme une panne. Elle lisait « kycApproved » dans la reponse de
        // verification OTP — un champ qu'Identity ne renvoie pas et ne peut
        // pas renvoyer : le statut appartient au service Driver. Le champ
        // valait donc null, donc false, et le bouton « passer en ligne »
        // restait grise meme apres validation.
        //
        // LE STATUT EST TRADUIT ICI, PAS RENVOYE BRUT. La passerelle
        // serialise les enums protobuf en entiers ; laisser passer un « 2 »
        // obligerait chaque client a connaitre la numerotation du contrat.
        group.MapGet("/me", async (
            HttpContext http,
            DriverService.DriverServiceClient drivers,
            CancellationToken cancellationToken) =>
        {
            var driver = await drivers.GetDriverAsync(
                new GetDriverRequest { DriverId = DriverIdOf(http) },
                cancellationToken: cancellationToken);

            return Results.Ok(new
            {
                id = driver.Id,
                displayName = driver.DisplayName,
                phone = driver.Phone,
                verificationStatus = driver.VerificationStatus.ToString(),
                operationalStatus = driver.OperationalStatus.ToString(),

                // Le seul booleen dont l'ecran d'accueil a besoin. Il est
                // calcule ici et non dans l'app : « peut travailler » est une
                // regle metier, pas une lecture d'enum.
                kycApproved = driver.VerificationStatus == VerificationStatus.Verified,
                statusReason = driver.StatusReason,

                // LE VEHICULE EST SOUVENT INCONNU, et l'app doit pouvoir le
                // dire. Le profil nait avec Vehicle.Unknown parce qu'aucune
                // route ne permet encore de le declarer ; renvoyer un objet
                // vide plutot que rien laisse l'ecran choisir sa phrase.
                vehicle = driver.Vehicle is null
                    ? null
                    : new
                    {
                        type = driver.Vehicle.Type.ToString(),
                        plate = driver.Vehicle.Plate,
                        capacityGrams = driver.Vehicle.CapacityGrams,
                    },

                // EN ISO, PAS EN { seconds, nanos }. Le reste de la
                // passerelle laisse System.Text.Json serialiser les
                // Timestamp protobuf, ce qui donne une forme que les clients
                // doivent dechiffrer. Ici, la conversion est faite une fois.
                registeredAt = driver.RegisteredAt?.ToDateTimeOffset(),
                verifiedAt = driver.VerifiedAt?.ToDateTimeOffset(),
            });
        });

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

        // LE RELEVE DU LIVREUR : ce qu'il a gagne, ce qui lui a ete verse, ce
        // qui reste du, et le detail ligne a ligne.
        //
        // AUCUN driver_id N'EST TRANSMIS, et c'est la garantie. Le service
        // prend celui du jeton : un livreur ne peut donc pas lire le compte
        // d'un collegue en changeant un parametre d'URL.
        //
        // LES MONTANTS SORTENT EN ENTIERS DE FRANCS, les dates en ISO. On ne
        // relaie pas le message protobuf tel quel : System.Text.Json rendrait
        // les enums en entiers et les dates en { seconds, nanos }, et
        // l'application devrait connaitre la numerotation du contrat pour
        // lire son propre releve.
        group.MapGet("/earnings", async (
            PaymentService.PaymentServiceClient payments,
            CancellationToken cancellationToken,
            int limit = 0) =>
        {
            var releve = await payments.GetDriverStatementAsync(
                new GetDriverStatementRequest { Limit = limit },
                cancellationToken: cancellationToken);

            return Results.Ok(new
            {
                earnedXof = releve.EarnedXof,
                paidOutXof = releve.PaidOutXof,
                dueXof = releve.DueXof,
                totalEntries = releve.TotalEntries,
                entries = releve.Entries.Select(entry => new
                {
                    id = entry.Id,
                    kind = Nommer(entry.Kind),
                    direction = Nommer(entry.Direction),
                    amountXof = entry.AmountXof,
                    deliveryId = entry.DeliveryId,
                    deliveryReference = entry.DeliveryReference,

                    // L'ORIGINE D'UNE SORTIE D'ARGENT. C'est ce qui permet a
                    // l'ecran des gains de poser la reference du virement sur
                    // la bonne ligne : sans lui, un debit serait un montant
                    // qui part sans que rien ne dise de quel versement il
                    // s'agit.
                    payoutId = entry.PayoutId,
                    occurredAt = entry.OccurredAt?.ToDateTimeOffset(),
                }),
            });
        });

        // LA DEMANDE DE VERSEMENT. Elle ne deplace aucun argent : elle ouvre
        // un dossier que la finance traite, et le grand livre ne bougera qu'au
        // moment ou le virement sera consigne.
        group.MapPost("/earnings/payouts", async (
            PayoutDto body,
            PaymentService.PaymentServiceClient payments,
            CancellationToken cancellationToken) =>
        {
            var demande = await payments.RequestPayoutAsync(
                new RequestPayoutRequest { AmountXof = body.AmountXof },
                cancellationToken: cancellationToken);

            return Results.Ok(Rendre(demande));
        });

        group.MapGet("/earnings/payouts", async (
            PaymentService.PaymentServiceClient payments,
            CancellationToken cancellationToken,
            int limit = 0) =>
        {
            var liste = await payments.ListDriverPayoutsAsync(
                new ListDriverPayoutsRequest { Limit = limit },
                cancellationToken: cancellationToken);

            return Results.Ok(new { payouts = liste.Payouts.Select(Rendre) });
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

    /// <summary>
    /// Une demande de versement, mise en forme.
    ///
    /// UNE DATE ABSENTE RESTE NULLE, et un Timestamp protobuf non renseigne
    /// rendrait le 1er janvier 1970 : « pas encore decide » se lirait comme
    /// une decision tres ancienne.
    /// </summary>
    private static object Rendre(PayoutRequest demande) => new
    {
        id = demande.Id,
        amountXof = demande.AmountXof,
        status = Nommer(demande.Status),
        requestedAt = demande.RequestedAt?.ToDateTimeOffset(),
        decidedAt = demande.DecidedAt?.ToDateTimeOffset(),
        rejectionReason = demande.RejectionReason,
        paidAt = demande.PaidAt?.ToDateTimeOffset(),
        paymentReference = demande.PaymentReference,
    };

    private static string Nommer(PayoutStatus statut) => statut switch
    {
        PayoutStatus.Requested => "PAYOUT_STATUS_REQUESTED",
        PayoutStatus.Approved => "PAYOUT_STATUS_APPROVED",
        PayoutStatus.Paid => "PAYOUT_STATUS_PAID",
        PayoutStatus.Rejected => "PAYOUT_STATUS_REJECTED",
        _ => "PAYOUT_STATUS_UNSPECIFIED",
    };

    // LES NOMS DU CONTRAT, PAS CEUX DU C# GENERE — meme convention que la
    // carte des positions du back-office. Un switch plutot que la reflexion :
    // si une valeur s'ajoute au contrat, celui-ci cesse de compiler, la ou la
    // reflexion rendrait une chaine vide en silence.
    private static string Nommer(LedgerEntryKind kind) => kind switch
    {
        LedgerEntryKind.DeliveryEarning => "LEDGER_ENTRY_KIND_DELIVERY_EARNING",
        LedgerEntryKind.Payout => "LEDGER_ENTRY_KIND_PAYOUT",
        _ => "LEDGER_ENTRY_KIND_UNSPECIFIED",
    };

    private static string Nommer(LedgerDirection direction) => direction switch
    {
        LedgerDirection.Credit => "LEDGER_DIRECTION_CREDIT",
        LedgerDirection.Debit => "LEDGER_DIRECTION_DEBIT",
        _ => "LEDGER_DIRECTION_UNSPECIFIED",
    };

    internal static string DriverIdOf(HttpContext http)
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

public sealed record PayoutDto(long AmountXof);
