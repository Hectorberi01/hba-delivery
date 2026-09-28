using Hba.BuildingBlocks.Application.Messaging;
using Hba.BuildingBlocks.Http;
using Hba.Media.Application.Assets;
using Hba.Media.Domain.Assets;
using Microsoft.AspNetCore.Mvc;

namespace Hba.Media.Api.Endpoints;

/// <summary>
/// Le dépôt d'un fichier.
///
/// EN HTTP, ET C'EST LE METIER DU SERVICE. L'ADR 0021 avait écarté le transport
/// de binaires par gRPC : un fichier qui traverse les intercepteurs de trace y
/// laisse ce qu'il ne devrait pas, et le typage de bout en bout ne protège rien
/// sur un flux d'octets. La route interne du service Driver était restée une
/// exception isolée, avec la consigne écrite de rouvrir l'ADR 0015 si une
/// seconde apparaissait. Media est la réponse à cette consigne : ici, une route
/// HTTP n'est plus une exception, c'est la raison d'être.
///
/// « internal » : jamais exposée directement au téléphone. La passerelle
/// relaie, en propageant le jeton de l'appelant.
/// </summary>
public static class MediaUploadEndpoints
{
    /// <summary>
    /// Plafond par fichier.
    ///
    /// UNE CNI PHOTOGRAPHIÉE FAIT TROIS À HUIT MÉGAOCTETS et le client paie ses
    /// données, souvent à la recharge. L'application réduit avant d'envoyer ;
    /// ce plafond attrape ce qui a échappé à la réduction, et protège le disque
    /// d'un envoi malveillant.
    /// </summary>
    public const long TailleMaximale = 5 * 1024 * 1024;

    /// <summary>
    /// Types acceptés.
    ///
    /// LISTE BLANCHE, PAS LISTE NOIRE. Une liste noire laisse passer ce qu'on
    /// n'a pas pensé à interdire ; c'est le stockage des pièces d'identité
    /// d'une plateforme, pas un espace de partage.
    /// </summary>
    private static readonly Dictionary<string, string> TypesAcceptes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["image/jpeg"] = ".jpg",
        ["image/png"] = ".png",
        ["application/pdf"] = ".pdf",
    };

    /// <summary>Natures pour lesquelles un PDF n'a aucun sens.</summary>
    private static readonly HashSet<MediaKind> ImagesSeulement =
    [
        MediaKind.ProfilePhoto,
        MediaKind.VehiclePhoto,
        MediaKind.DeliveryProof,
    ];

    public static IEndpointRouteBuilder MapMediaUploadEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app.MapGroup("/internal/v1/media")
            .RequireAuthorization()
            // SANS CE FILTRE, UN REFUS METIER SORT EN 500 AVEC UN CORPS VIDE :
            // les routes gRPC ont leur intercepteur, celles-ci n'ont que lui.
            .AddEndpointFilter<TraduireLesRefus>();

        group.MapPost("/", async (
            [FromQuery] string ownerType,
            [FromQuery] string ownerId,
            [FromQuery] string kind,
            IFormFile fichier,
            IDispatcher dispatcher,
            CancellationToken cancellationToken) =>
        {
            if (!Enum.TryParse<MediaOwnerType>(ownerType, ignoreCase: true, out var proprietaire)
                || proprietaire == MediaOwnerType.Unspecified)
            {
                return Refus(
                    "UNKNOWN_OWNER_TYPE",
                    $"Type de propriétaire inconnu : « {ownerType} ».",
                    Enum.GetNames<MediaOwnerType>());
            }

            if (!Enum.TryParse<MediaKind>(kind, ignoreCase: true, out var nature)
                || nature == MediaKind.Unspecified)
            {
                return Refus(
                    "UNKNOWN_MEDIA_KIND",
                    $"Nature de média inconnue : « {kind} ».",
                    Enum.GetNames<MediaKind>());
            }

            var refus = Verifier(fichier, ImagesSeulement.Contains(nature));
            if (refus is not null)
            {
                return refus;
            }

            await using var flux = fichier.OpenReadStream();

            var view = await dispatcher.SendAsync(
                new StoreMediaCommand(
                    proprietaire,
                    ownerId,
                    nature,
                    flux,
                    fichier.Length,
                    fichier.ContentType,
                    TypesAcceptes[fichier.ContentType]),
                cancellationToken).ConfigureAwait(false);

            return Results.Ok(new
            {
                id = view.Id,
                ownerType = view.OwnerType.ToString(),
                ownerId = view.OwnerId,
                kind = view.Kind.ToString(),
                contentType = view.ContentType,
                sizeBytes = view.SizeBytes,
                createdAt = view.CreatedAt,
            });
        })
        // LE PLAFOND EST POSE DEUX FOIS : ici, ou Kestrel refuse le corps avant
        // de l'avoir lu, et dans Verifier, qui rend un message exploitable.
        // Sans le premier, un envoi de 400 Mo occupe la memoire du service le
        // temps d'etre rejete.
        .WithMetadata(new RequestSizeLimitAttribute(TailleMaximale + 64 * 1024))
        .DisableAntiforgery();

        return app;
    }

    private static IResult? Verifier(IFormFile fichier, bool imagesSeulement)
    {
        if (fichier is null || fichier.Length == 0)
        {
            return Refus("EMPTY_FILE", "Le fichier reçu est vide.");
        }

        if (fichier.Length > TailleMaximale)
        {
            return Refus(
                "FILE_TOO_LARGE",
                $"Le fichier fait {fichier.Length / 1024 / 1024} Mo ; la limite est de "
                + $"{TailleMaximale / 1024 / 1024} Mo.");
        }

        if (!TypesAcceptes.ContainsKey(fichier.ContentType))
        {
            return Refus(
                "UNSUPPORTED_MEDIA_TYPE",
                $"Type de fichier refusé : « {fichier.ContentType} ».",
                TypesAcceptes.Keys);
        }

        if (imagesSeulement && !fichier.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            return Refus("IMAGE_REQUIRED", "Cette nature de média n'accepte que des images.");
        }

        return null;
    }

    private static IResult Refus(string code, string message, IEnumerable<string>? attendu = null)
        => Results.BadRequest(new { code, message, attendu });
}
