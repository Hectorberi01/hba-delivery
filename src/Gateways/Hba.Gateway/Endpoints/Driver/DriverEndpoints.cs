using Hba.BuildingBlocks.Security;
using Hba.Contracts.Common.V1;
using Hba.Contracts.Delivery.V1;
using Hba.Contracts.Dispatch.V1;
using Hba.Contracts.Driver.V1;
using Hba.Contracts.Location.V1;
using Hba.Contracts.Payment.V1;
using Hba.Gateway.Endpoints.Relais;
using Microsoft.AspNetCore.Mvc;

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
        // CREER SON PROFIL, QUAND L'INSCRIPTION NE L'A PAS FAIT.
        //
        // Le profil livreur nait d'un evenement : Identity publie
        // AccountRegistered, Driver le consomme. Cet evenement se perd — Kafka
        // ne rejoue pas pour un groupe de consommateurs qui n'existait pas
        // encore —, et le livreur se retrouve alors avec un compte valide, un
        // jeton portant driver_id, et un GET /me qui repond 404 a vie. Le
        // client avait deja cette porte de secours ; le livreur non.
        //
        // AUCUN CORPS : le service relit le jeton qu'il a lui-meme valide et en
        // tire le nom et le telephone. Idempotente.
        group.MapPost("/me", async (
            DriverService.DriverServiceClient drivers,
            CancellationToken cancellationToken) =>
        {
            var driver = await drivers.EnsureDriverAsync(
                new EnsureDriverRequest(),
                cancellationToken: cancellationToken);

            return Results.Ok(new
            {
                id = driver.Id,
                displayName = driver.DisplayName,
                phone = driver.Phone,
                verificationStatus = driver.VerificationStatus.ToString(),
                operationalStatus = driver.OperationalStatus.ToString(),
                kycApproved = driver.VerificationStatus == VerificationStatus.Verified,
                statusReason = driver.StatusReason,
            });
        });

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

        // PASSER EN LIGNE DEMANDE MAINTENANT DEUX APPELS, ET L'ORDRE COMPTE.
        //
        // Driver porte la presence DECLAREE — « je me rends disponible », un etat metier
        // qui s'audite et survit a un telephone eteint. Location porte la presence
        // TECHNIQUE — « mon telephone emet », qui s'evapore au bout de la fenetre de
        // fraicheur. Jusqu'au 1er octobre 2026 les deux etaient ecrites par Driver dans
        // la foulee ; depuis que les positions ont demenage, c'est cette route qui
        // enchaine.
        //
        // LE POINT APRES L'ETAT, JAMAIS AVANT. Poser la position d'abord rendrait
        // joignable un livreur dont le passage en ligne vient d'etre refuse — dossier non
        // valide, compte suspendu. C'est la meme precaution que prenait l'ancien handler,
        // deplacee ici avec le geste.
        //
        // SI LE SECOND APPEL ECHOUE, LE LIVREUR EST EN LIGNE SANS ETRE SUR LA CARTE, donc
        // aucune vague ne le trouve. C'est exactement l'etat d'un livreur dont le
        // telephone n'a pas encore envoye son premier battement : un etat normal, qui se
        // resout au battement suivant, en quelques secondes. On ne rend donc pas d'erreur
        // au livreur pour cela.
        group.MapPost("/presence/online", async (
            PositionDto body,
            HttpContext http,
            DriverService.DriverServiceClient drivers,
            LocationService.LocationServiceClient locations,
            CancellationToken cancellationToken) =>
        {
            var driverId = DriverIdOf(http);

            var driver = await drivers.GoOnlineAsync(
                new GoOnlineRequest { DriverId = driverId },
                cancellationToken: cancellationToken);

            await locations.UpdatePositionAsync(
                PositionRequest(driverId, body),
                cancellationToken: cancellationToken);

            return Results.Ok(driver);
        });

        // L'OUBLI DU POINT EST DE L'HYGIENE D'AFFICHAGE, PLUS UNE DECISION.
        //
        // Avant le 1er octobre 2026, effacer la position etait CE QUI mettait quelqu'un
        // hors du dispatch : l'oublier laissait un livreur rentre chez lui recevoir des
        // offres. Aujourd'hui la vague croise toujours Driver, qui le voit hors ligne —
        // un point qui traine ne produit plus aucune offre par lui-meme.
        //
        // ON L'EFFACE QUAND MEME, pour la carte de l'exploitation : sans cela, ops verrait
        // encore pendant deux minutes quelqu'un qui vient de se deconnecter. Et c'est
        // precisement parce que ce geste ne porte plus de decision qu'il peut vivre ici,
        // a la passerelle, sans risque.
        group.MapPost("/presence/offline", async (
            HttpContext http,
            DriverService.DriverServiceClient drivers,
            LocationService.LocationServiceClient locations,
            CancellationToken cancellationToken) =>
        {
            var driverId = DriverIdOf(http);

            var driver = await drivers.GoOfflineAsync(
                new GoOfflineRequest { DriverId = driverId },
                cancellationToken: cancellationToken);

            await locations.ForgetPositionAsync(
                new ForgetPositionRequest { DriverId = driverId },
                cancellationToken: cancellationToken);

            return Results.Ok(driver);
        });

        // LE BATTEMENT DE POSITION. C'EST L'APPEL LE PLUS FREQUENT DE TOUTE LA
        // PLATEFORME : un point toutes les quelques secondes, par livreur en ligne. Il va
        // droit a Location, qui n'a ni base relationnelle ni lecture d'agregat a faire.
        group.MapPost("/position", async (
            PositionDto body,
            HttpContext http,
            LocationService.LocationServiceClient locations,
            CancellationToken cancellationToken) =>
        {
            await locations.UpdatePositionAsync(
                PositionRequest(DriverIdOf(http), body),
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
                    // LES DEUX NOMS SORTENT, ET C'EST TEMPORAIRE.
                    //
                    // Le grand livre connaissait la livraison ; il connait
                    // desormais la mission, course de personnes comprise. Les
                    // champs « reference* » sont les bons ; « delivery* » restent
                    // le temps que l'application livreur passe aux premiers.
                    // Supprimer les anciens aujourd'hui viderait l'ecran des gains
                    // sur les telephones deja installes.
                    serviceType = Nommer(entry.ServiceType),
                    referenceId = entry.ReferenceId,
                    referenceLabel = entry.ReferenceLabel,
                    deliveryId = entry.ReferenceId,
                    deliveryReference = entry.ReferenceLabel,

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

        // LA PHOTO D'UNE ETAPE, ET ELLE NE PASSE PAS PAR LA MEME PORTE.
        //
        // Les quatre actions ci-dessus sont du gRPC : un identifiant, un
        // horodatage, une cle d'idempotence. Celle-ci porte des OCTETS, et gRPC
        // les porte mal (ADR 0021) — d'ou un relais HTTP, comme pour les pieces
        // du dossier livreur et la photo de profil du client.
        //
        // ELLE VA VERS DELIVERY, PAS VERS MEDIA, et c'est la decision du
        // 30 septembre 2026 : « ce livreur est-il affecte a cette course » est
        // une question que seul Delivery sait trancher. La passerelle ne detient
        // aucun jeton de service et n'en detiendra pas : elle reporte celui du
        // livreur, et rien d'autre.
        //
        // ELLE NE PORTE NI HORODATAGE NI CLE D'IDEMPOTENCE, contrairement a ses
        // voisines. Une photo n'entre jamais dans la file hors ligne — cette
        // file range du JSON dans le coffre chiffre et ne sait pas transporter
        // un fichier —, donc un depot se fait toujours en direct.
        group.MapPost("/missions/{id}/proof", async (
            string id,
            [FromQuery] string etape,
            HttpRequest requete,
            HttpContext http,
            IPreuveRelay preuves,
            CancellationToken cancellationToken) =>
            await preuves.DeposerAsync(requete, http, id, etape, cancellationToken))
        .DisableAntiforgery();

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

        // L'INCIDENT : LA SEULE SORTIE QUAND LA REMISE EST IMPOSSIBLE.
        //
        // Destinataire absent, adresse fausse, code bloque apres cinq essais :
        // le livreur n'avait aucune porte. Il gardait le colis, restait en
        // mission, et ne recevait plus d'offre jusqu'a ce qu'un ops cloture a sa
        // place. La course part en FAILED ; « remise » reste hors de sa portee.
        group.MapPost("/missions/{id}/incident", async (
            string id,
            IncidentDto body,
            HttpContext http,
            DeliveryService.DeliveryServiceClient deliveries,
            CancellationToken cancellationToken) =>
        {
            var result = await deliveries.DeclareIncidentAsync(
                new DeclareIncidentRequest
                {
                    DeliveryId = id,
                    Reason = body.Reason,
                    OccurredAt = ToTimestamp(body.OccurredAt),
                    IdempotencyKey = IdempotencyKeyOf(http),
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
    /// <summary>
    /// La nature de la mission remuneree, en texte stable. Vide pour un versement,
    /// qui solde un cumul et non une mission en particulier.
    /// </summary>
    private static string? Nommer(Hba.Contracts.Common.V1.ServiceType nature) => nature switch
    {
        Hba.Contracts.Common.V1.ServiceType.Delivery => "DELIVERY",
        Hba.Contracts.Common.V1.ServiceType.Ride => "RIDE",
        _ => null,
    };

    private static string Nommer(LedgerEntryKind kind) => kind switch
    {
        LedgerEntryKind.DeliveryEarning => "LEDGER_ENTRY_KIND_DELIVERY_EARNING",
        LedgerEntryKind.Payout => "LEDGER_ENTRY_KIND_PAYOUT",
        LedgerEntryKind.Compensation => "LEDGER_ENTRY_KIND_COMPENSATION",
        LedgerEntryKind.Tip => "LEDGER_ENTRY_KIND_TIP",
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

    /// <summary>
    /// Traduit un point recu en JSON vers le contrat de Location.
    /// </summary>
    ///
    /// <remarks>
    /// LES TROIS CHAMPS FACULTATIFS NE SONT POSES QUE S'ILS EXISTENT, et c'est tout
    /// l'interet de « optional » dans le contrat. Les affecter a zero quand ils sont
    /// absents les rendrait PRESENTS : Location recevrait un cap plein nord et une
    /// vitesse nulle la ou le telephone n'a rien dit, et toutes les motos immobiles
    /// pointeraient vers le nord sur la carte de l'exploitation.
    ///
    /// L'HORODATAGE ABSENT VAUT MAINTENANT, ET IL NE DECIDE RIEN DE TOUTE FACON.
    /// Location se fie a l'heure de RECEPTION pour la fraicheur : celle du telephone
    /// n'est transportee que pour comprendre, apres coup, pourquoi un point est arrive
    /// en retard.
    /// </remarks>
    private static UpdatePositionRequest PositionRequest(string driverId, PositionDto corps)
    {
        var demande = new UpdatePositionRequest
        {
            DriverId = driverId,
            Point = new GeoPoint { Latitude = corps.Latitude, Longitude = corps.Longitude },
            CapturedAt = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTimeOffset(
                corps.CapturedAt ?? DateTimeOffset.UtcNow),
        };

        if (corps.HeadingDegrees is { } cap)
        {
            demande.HeadingDegrees = cap;
        }

        if (corps.SpeedMps is { } vitesse)
        {
            demande.SpeedMps = vitesse;
        }

        if (corps.AccuracyMeters is { } precision)
        {
            demande.AccuracyMeters = precision;
        }

        return demande;
    }
}

/// <summary>
/// Un point envoye par l'application livreur.
/// </summary>
///
/// <remarks>
/// LES TROIS DERNIERS CHAMPS SONT ARRIVES LE 1er OCTOBRE 2026, ET DEUX EXISTAIENT DEJA
/// AU CONTRAT gRPC DEPUIS LE DEBUT. « heading_degrees » et « speed_mps » etaient dans
/// UpdateLocationRequest, et personne ne les remplissait : ni cette passerelle, ni
/// l'application. La precision GPS, elle, n'existait nulle part. C'est pour cela
/// qu'aucune carte n'a jamais pu orienter une fleche.
///
/// ILS SONT FACULTATIFS, ET LE NUL N'EST PAS ZERO. Un telephone a l'arret ne rend aucun
/// cap ; zero degre, lui, veut dire « plein nord ». En JSON l'absence du champ et sa
/// valeur nulle se confondent, ce qui est exactement ce qu'on veut : les deux
/// signifient « le telephone n'a rien dit ».
/// </remarks>
public sealed record PositionDto(
    double Latitude,
    double Longitude,
    DateTimeOffset? CapturedAt,
    double? HeadingDegrees = null,
    double? SpeedMps = null,
    double? AccuracyMeters = null);

public sealed record DeclineDto(string? Reason);

public sealed record TimestampedDto(DateTimeOffset? OccurredAt);

public sealed record ProofDto(DateTimeOffset? OccurredAt, string? ProofObjectKey);

public sealed record ConfirmDto(string Otp, DateTimeOffset? OccurredAt, string? ProofObjectKey);

/// <summary>Motif libre : aucune liste d'incidents n'est tranchee.</summary>
public sealed record IncidentDto(string Reason, DateTimeOffset? OccurredAt);

public sealed record PayoutDto(long AmountXof);
