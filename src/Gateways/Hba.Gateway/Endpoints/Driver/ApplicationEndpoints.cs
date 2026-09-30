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
                },
                cancellationToken: cancellationToken);

            return Results.Ok(driver);
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
    };
}

public sealed record VehiculeDto(string Type, string? Plate, int? CapacityGrams);
