using Hba.BuildingBlocks.Security;
using Hba.Contracts.Common.V1;
using Hba.Contracts.Driver.V1;

namespace Hba.Gateway.Endpoints.Driver;

/// <summary>
/// Le dossier du livreur, vu de l'application.
///
/// UNE SEULE DE CES ROUTES NE PASSE PAS PAR gRPC : le dépôt d'une pièce. Les
/// octets sont relayés tels quels vers le service Driver, qui écrit dans le
/// stockage et enregistre la pièce ensemble (ADR 0021). La passerelle ne
/// connaît ni les identifiants du stockage, ni la convention de clés.
/// </summary>
public static class ApplicationEndpoints
{
    public static IEndpointRouteBuilder MapDriverApplicationEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app.MapGroup("/api/driver/v1").RequireAuthorization(HbaPolicies.Driver);

        group.MapGet("/application", async (
            HttpContext http,
            DriverService.DriverServiceClient drivers,
            CancellationToken cancellationToken) =>
        {
            var dossier = await drivers.GetApplicationAsync(
                new GetDriverRequest { DriverId = DriverEndpoints.DriverIdOf(http) },
                cancellationToken: cancellationToken);

            return Results.Ok(Traduire(dossier));
        });

        group.MapPut("/vehicle", async (
            VehiculeDto body,
            HttpContext http,
            DriverService.DriverServiceClient drivers,
            CancellationToken cancellationToken) =>
        {
            var driver = await drivers.DeclareVehicleAsync(
                new DeclareVehicleRequest
                {
                    DriverId = DriverEndpoints.DriverIdOf(http),
                    Type = body.Type switch
                    {
                        "MOTORCYCLE" => VehicleType.Motorcycle,
                        "CAR" => VehicleType.Car,
                        "VAN" => VehicleType.Van,
                        "BICYCLE" => VehicleType.Bicycle,
                        "TRICYCLE" => VehicleType.Tricycle,
                        _ => VehicleType.Unspecified,
                    },
                    Plate = body.Plate ?? string.Empty,
                    CapacityGrams = body.CapacityGrams ?? 0,

                    // LES QUATRE CHAMPS QUE CETTE ROUTE JETAIT. Le contrat les
                    // porte depuis le 1er octobre 2026 et le domaine les
                    // accepte ; la passerelle, elle, n'envoyait que le type, la
                    // plaque et la charge. Conséquence : aucun livreur ne
                    // pouvait déclarer de place passager, donc
                    // PeutTransporterDesPersonnes était faux pour tout le monde
                    // et le métier course restait fermé — et la marque, le
                    // modèle et la couleur, que le profil public rend au client
                    // pour qu'il reconnaisse « la voiture blanche », restaient
                    // vides pour toujours.
                    //
                    // LE SERVICE REFUSE UNE PLACE SUR UN VÉHICULE QUI N'EN PORTE
                    // PAS (VEHICLE_CANNOT_CARRY_PASSENGERS) : la passerelle ne
                    // présume rien et transmet ce qui a été saisi.
                    Brand = body.Brand ?? string.Empty,
                    Model = body.Model ?? string.Empty,
                    Color = body.Color ?? string.Empty,
                    PassengerCapacity = body.PassengerCapacity ?? 0,
                },
                cancellationToken: cancellationToken);

            return Results.Ok(driver);
        });

        // LE LIVREUR DÉCLARE SES HABILITATIONS, OPS VALIDE PAR LE KYC (point 38).
        // Il n'y a pas de route back-office jumelle, et c'est la décision du
        // 1er octobre 2026 : l'examen d'ops est le KYC, qui porte sur le dossier
        // entier. Le service accepte pourtant un jeton d'exploitation
        // (EnsureSelfOrBackOffice) et doit le garder — l'autorisation se vérifie
        // côté service, jamais seulement ici (ADR 0007) — de sorte qu'ouvrir
        // cette route à ops un jour ne demandera pas de toucher au domaine.
        //
        // UN DOSSIER VALIDÉ SE DÉCLARE LIBREMENT DEPUIS LE 3 OCTOBRE 2026, et
        // sans repasser à l'examen. Ce commentaire disait l'inverse, à juste
        // titre jusque-là. Ce qui a changé : cocher « transport de personnes »
        // n'apporte aucune pièce nouvelle, le véhicule qui l'autorise a déjà été
        // vérifié, et le domaine vérifie la table de Vehicle.Autorise. Seule la
        // suspension bloque encore. Changer de VÉHICULE, lui, reste payant : il
        // renvoie le dossier à l'examen.
        group.MapPut("/capabilities", async (
            HabilitationsDto body,
            HttpContext http,
            DriverService.DriverServiceClient drivers,
            CancellationToken cancellationToken) =>
        {
            var requete = new DeclareCapabilitiesRequest
            {
                DriverId = DriverEndpoints.DriverIdOf(http),
            };

            // L'ENSEMBLE COMPLET, PAS UN AJOUT : un corps vide retire tout et
            // ramène le livreur au seul colis. C'est le contrat qui le dit, et
            // retirer une habilitation doit être aussi simple que l'accorder.
            foreach (var nom in body.Capabilities ?? [])
            {
                var habilitation = Habilitation(nom);

                // UN NOM INCONNU EST REFUSÉ, IL N'EST PAS IGNORÉ. Le domaine
                // écarte UNSPECIFIED en silence : une faute de frappe
                // produirait un enregistrement réussi auquel il manquerait une
                // ligne, et personne ne saurait pourquoi.
                if (habilitation == DriverCapability.Unspecified)
                {
                    return Results.BadRequest(new
                    {
                        code = "UNKNOWN_CAPABILITY",
                        message = $"Habilitation inconnue : {nom}.",
                    });
                }

                requete.Capabilities.Add(habilitation);
            }

            await drivers.DeclareCapabilitiesAsync(requete, cancellationToken: cancellationToken);

            // RIEN À RENDRE : l'application recharge le dossier juste après, et
            // rendre l'agrégat ici enverrait ses énumérations en entiers — le
            // piège que Traduire existe pour éviter.
            return Results.NoContent();
        });

        group.MapPost("/application/submit", async (
            HttpContext http,
            DriverService.DriverServiceClient drivers,
            CancellationToken cancellationToken) =>
        {
            var driver = await drivers.SubmitApplicationAsync(
                new SubmitApplicationRequest { DriverId = DriverEndpoints.DriverIdOf(http) },
                cancellationToken: cancellationToken);

            return Results.Ok(driver);
        });

        // --- Le seul relais d'octets de la passerelle ---

        group.MapPost("/documents", async (
            HttpRequest requete,
            HttpContext http,
            IDriverUploadRelay relais,
            CancellationToken cancellationToken) =>
                await relais.RelayerAsync(
                    requete,
                    http,
                    $"documents?type={Uri.EscapeDataString(requete.Query["type"].FirstOrDefault() ?? string.Empty)}",
                    cancellationToken))
            .DisableAntiforgery();

        group.MapPost("/profile-photo", async (
            HttpRequest requete,
            HttpContext http,
            IDriverUploadRelay relais,
            CancellationToken cancellationToken) =>
                await relais.RelayerAsync(requete, http, "profile-photo", cancellationToken))
            .DisableAntiforgery();

        // --- Back-office : ops doit VOIR les pièces avant de décider ---

        var backOffice = app.MapGroup("/api/admin/v1/drivers/{driverId}")
            .RequireAuthorization(HbaPolicies.BackOffice);

        backOffice.MapGet("/application", async (
            string driverId,
            DriverService.DriverServiceClient drivers,
            CancellationToken cancellationToken) =>
        {
            var dossier = await drivers.GetApplicationAsync(
                new GetDriverRequest { DriverId = driverId },
                cancellationToken: cancellationToken);

            return Results.Ok(Traduire(dossier));
        });

        return app;
    }

    /// <summary>
    /// Les enums sortent par leur NOM, pas par leur numéro.
    ///
    /// LE RESTE DE LA PASSERELLE LES REND EN ENTIERS, parce qu'elle laisse
    /// System.Text.Json sérialiser les messages protobuf. C'est un piège
    /// connu de ce dépôt — il a déjà coûté un écran de livreur muet. Ces
    /// routes-ci sont neuves : elles n'ont aucune raison d'hériter du piège,
    /// et un client n'a pas à connaître la numérotation du contrat.
    /// </summary>
    private static object Traduire(DriverApplication dossier) => new
    {
        driverId = dossier.DriverId,
        verificationStatus = dossier.VerificationStatus.ToString(),
        statusReason = dossier.StatusReason,
        submittedAt = dossier.SubmittedAt?.ToDateTimeOffset(),
        canSubmit = dossier.CanSubmit,
        profilePhotoUrl = dossier.ProfilePhotoUrl,
        vehicle = dossier.Vehicle is null
            ? null
            : new
            {
                type = dossier.Vehicle.Type.ToString(),
                plate = dossier.Vehicle.Plate,
                capacityGrams = dossier.Vehicle.CapacityGrams,
                brand = dossier.Vehicle.Brand,
                model = dossier.Vehicle.Model,
                color = dossier.Vehicle.Color,
                passengerCapacity = dossier.Vehicle.PassengerCapacity,
            },
        documents = dossier.Documents.Select(d => new
        {
            type = d.Type.ToString(),
            uploadedAt = d.UploadedAt?.ToDateTimeOffset(),
            sizeBytes = d.SizeBytes,
            contentType = d.ContentType,
            readUrl = d.ReadUrl,
            readUrlExpiresAt = d.ReadUrlExpiresAt?.ToDateTimeOffset(),
        }),
        missingDocuments = dossier.MissingDocuments.Select(t => t.ToString()),

        // LES PIECES EXIGEES POUR CE VEHICULE-LA, et non les cinq en dur que
        // l'application affichait. Un cycliste y voyait « permis » et « carte
        // grise », deux lignes qu'il n'aurait jamais pu satisfaire.
        requiredDocuments = dossier.RequiredDocuments.Select(t => t.ToString()),

        // LE VEHICULE EST-IL DECLARE. L'application le devinait par « la plaque
        // n'est pas vide » ; un velo n'en a pas.
        vehicleDeclared = dossier.VehicleDeclared,

        // CE QUE LE LIVREUR A DECLARE EXERCER. Vide veut dire « le colis » :
        // c'est le socle, il ne se declare pas.
        capabilities = dossier.Capabilities.Select(c => c.ToString()),

        // CE QUE SON VEHICULE PERMET, calcule par le service. L'ecran coche
        // dans cette liste et nulle part ailleurs : une liste codee en dur dans
        // l'application proposerait « transport de personnes » a un cycliste.
        //
        // ELLE CONTIENT LE COLIS, et l'ecran ne doit pas l'afficher comme une
        // case : le service repond a « que permet ce vehicule », pas a « que
        // faut-il montrer ».
        allowedCapabilities = dossier.AllowedCapabilities.Select(c => c.ToString()),
    };

    /// <summary>
    /// Le nom d'une habilitation, dans l'orthographe que le client veut bien
    /// employer.
    ///
    /// ON ACCEPTE LES TROIS ORTHOGRAPHES parce que ce depot en fait circuler
    /// deux : les reponses rendent « PassengerTransport » (le nom C# de
    /// l'enumeration protobuf) et la route du vehicule attend « CAR » en
    /// majuscules. Une application qui renvoie telle quelle la liste recue doit
    /// marcher, et une autre qui suit la convention du vehicule aussi.
    /// Normaliser ici coute une ligne ; un refus incomprehensible coute une
    /// journee.
    ///
    /// UNSPECIFIED SIGNALE L'INCONNU, il ne vaut pas « rien » : l'appelant le
    /// traduit en refus.
    /// </summary>
    private static DriverCapability Habilitation(string? nom) =>
        new string((nom ?? string.Empty).Where(char.IsLetter).ToArray()).ToUpperInvariant() switch
        {
            "PACKAGEDELIVERY" => DriverCapability.PackageDelivery,
            "FOODDELIVERY" => DriverCapability.FoodDelivery,
            "PASSENGERTRANSPORT" => DriverCapability.PassengerTransport,
            _ => DriverCapability.Unspecified,
        };
}

public sealed record VehiculeDto(
    string Type,
    string? Plate,
    int? CapacityGrams,
    string? Brand = null,
    string? Model = null,
    string? Color = null,
    int? PassengerCapacity = null);

/// <summary>
/// L'ENSEMBLE COMPLET DES HABILITATIONS, pas un ajout. Absent ou vide, il les
/// retire toutes et ramene le livreur au seul colis.
/// </summary>
public sealed record HabilitationsDto(IReadOnlyList<string>? Capabilities);
