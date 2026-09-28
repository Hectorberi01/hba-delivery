using Hba.BuildingBlocks.Application.Messaging;
using Hba.BuildingBlocks.Http;
using Hba.Driver.Application.Features.Drivers.Commands;
using Hba.Driver.Domain.Drivers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hba.Driver.Api.Endpoints;

/// <summary>
/// Dépôt des pièces du dossier.
///
/// LA SEULE ROUTE HTTP INTERNE DU DÉPÔT, et c'est une exception assumée à
/// l'ADR 0002 — motivée dans l'ADR 0021. Un binaire qui traverse gRPC
/// traverse aussi les intercepteurs de trace, où une pièce d'identité n'a
/// rien à faire ; et le typage de bout en bout que défend l'ADR 0015 ne
/// protège rien sur un flux d'octets.
///
/// Si une deuxième route interne en HTTP se présente, ce n'est plus une
/// exception mais une tendance, et l'ADR 0015 devra être rouvert.
/// </summary>
public static class DocumentEndpoints
{
    /// <summary>
    /// Plafond par pièce.
    ///
    /// UNE CNI PHOTOGRAPHIÉE FAIT TROIS À HUIT MÉGAOCTETS et le livreur paie
    /// ses données, souvent à la recharge. L'application réduit avant
    /// d'envoyer ; ce plafond attrape ce qui a échappé à la réduction, et
    /// protège le disque d'un envoi malveillant. Une pièce d'identité reste
    /// parfaitement lisible bien en deçà.
    /// </summary>
    public const long TailleMaximale = 5 * 1024 * 1024;

    /// <summary>
    /// Types acceptés.
    ///
    /// LISTE BLANCHE, PAS LISTE NOIRE. Une liste noire laisse passer ce
    /// qu'on n'a pas pensé à interdire ; c'est le stockage des pièces
    /// d'identité d'une plateforme, pas un espace de partage.
    /// </summary>
    private static readonly Dictionary<string, string> TypesAcceptes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["image/jpeg"] = ".jpg",
        ["image/png"] = ".png",
        ["application/pdf"] = ".pdf",
    };

    public static IEndpointRouteBuilder MapDriverDocumentEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        // « internal » : cette route n'est jamais exposée au téléphone
        // directement. La passerelle est seule à l'appeler, en relayant le
        // jeton du livreur — l'autorisation reste vérifiée dans le handler.
        var group = app.MapGroup("/internal/v1/drivers/{driverId:guid}")
            .RequireAuthorization()
            // SANS CE FILTRE, UN REFUS METIER SORT EN 500 AVEC UN CORPS VIDE.
            // Les routes gRPC ont leur intercepteur ; celles-ci n'avaient
            // rien, et la passerelle relayait donc du vide au livreur.
            .AddEndpointFilter<TraduireLesRefus>();

        group.MapPost("/documents", async (
            Guid driverId,
            [FromQuery] string type,
            IFormFile fichier,
            IDispatcher dispatcher,
            CancellationToken cancellationToken) =>
        {
            if (!TypeDePiece(type, out var nature))
            {
                return Results.BadRequest(new
                {
                    code = "UNKNOWN_DOCUMENT_TYPE",
                    message = $"Nature de pièce inconnue : « {type} ».",
                    attendu = Enum.GetNames<DocumentType>().Where(n => n != nameof(DocumentType.Unspecified)),
                });
            }

            var refus = Verifier(fichier);
            if (refus is not null)
            {
                return refus;
            }

            await using var flux = fichier.OpenReadStream();

            var view = await dispatcher.SendAsync(
                new UploadDocumentCommand(
                    driverId,
                    nature,
                    flux,
                    fichier.Length,
                    fichier.ContentType,
                    TypesAcceptes[fichier.ContentType]),
                cancellationToken).ConfigureAwait(false);

            return Results.Ok(view);
        })
        // Le plafond est posé DEUX FOIS : ici, où Kestrel refuse le corps
        // avant de l'avoir lu, et dans Verifier, qui rend un message
        // exploitable. Sans le premier, un envoi de 400 Mo occupe la mémoire
        // du service le temps d'être rejeté.
        .WithMetadata(new RequestSizeLimitAttribute(TailleMaximale + 64 * 1024))
        .DisableAntiforgery();

        group.MapPost("/profile-photo", async (
            Guid driverId,
            IFormFile fichier,
            IDispatcher dispatcher,
            CancellationToken cancellationToken) =>
        {
            var refus = Verifier(fichier, imagesSeulement: true);
            if (refus is not null)
            {
                return refus;
            }

            await using var flux = fichier.OpenReadStream();

            var view = await dispatcher.SendAsync(
                new UploadProfilePhotoCommand(
                    driverId,
                    flux,
                    fichier.Length,
                    fichier.ContentType,
                    TypesAcceptes[fichier.ContentType]),
                cancellationToken).ConfigureAwait(false);

            return Results.Ok(view);
        })
        .WithMetadata(new RequestSizeLimitAttribute(TailleMaximale + 64 * 1024))
        .DisableAntiforgery();

        return app;
    }

    /// <summary>
    /// Lit la nature d'une pièce, dans l'une ou l'autre écriture.
    ///
    /// DEUX NOMS POUR LA MEME CHOSE, ET C'EST LE CONTRAT QUI LE VEUT :
    /// protobuf nomme la valeur `DOCUMENT_TYPE_NATIONAL_ID`, le C# généré
    /// l'appelle `NationalId`. Un client écrit en lisant le `.proto` envoie
    /// la première ; `Enum.TryParse` ne connaît que la seconde, et refuse
    /// en disant « nature inconnue » — ce qui envoie chercher une faute de
    /// frappe là où il n'y en a pas.
    ///
    /// On retire donc les tirets bas avant de comparer. « NATIONAL_ID »,
    /// « NationalId » et « national_id » désignent la même pièce, parce
    /// qu'ils la désignent vraiment.
    /// </summary>
    private static bool TypeDePiece(string? brut, out DocumentType nature)
    {
        nature = DocumentType.Unspecified;

        if (string.IsNullOrWhiteSpace(brut))
        {
            return false;
        }

        var normalise = brut.Replace("_", string.Empty, StringComparison.Ordinal);

        return Enum.TryParse(normalise, ignoreCase: true, out nature)
               && nature != DocumentType.Unspecified;
    }

    /// <summary>
    /// Rend un refus, ou null si le fichier passe.
    ///
    /// LE TYPE ANNONCÉ N'EST PAS VÉRIFIÉ CONTRE LE CONTENU. Un fichier
    /// déclaré image/jpeg peut être n'importe quoi : seul un examen des
    /// premiers octets le dirait. Ce n'est pas fait ici, et il faut le savoir
    /// — le stockage ne sert jamais ces objets au navigateur autrement que
    /// par une URL signée, ce qui limite la portée, sans l'annuler.
    /// </summary>
    private static IResult? Verifier(IFormFile? fichier, bool imagesSeulement = false)
    {
        if (fichier is null || fichier.Length == 0)
        {
            return Results.BadRequest(new { code = "EMPTY_DOCUMENT", message = "Fichier vide ou absent." });
        }

        if (fichier.Length > TailleMaximale)
        {
            return Results.BadRequest(new
            {
                code = "DOCUMENT_TOO_LARGE",
                message = $"La pièce dépasse {TailleMaximale / (1024 * 1024)} Mo. Réduisez la photo avant de l'envoyer.",
                maxBytes = TailleMaximale,
            });
        }

        if (!TypesAcceptes.ContainsKey(fichier.ContentType ?? string.Empty))
        {
            return Results.BadRequest(new
            {
                code = "UNSUPPORTED_MEDIA_TYPE",
                message = "Formats acceptés : JPEG, PNG, PDF.",
            });
        }

        // Un PDF ne fait pas une photo de profil : le client doit reconnaître
        // un visage d'un coup d'oeil, pas ouvrir un document.
        if (imagesSeulement && fichier.ContentType!.Equals("application/pdf", StringComparison.OrdinalIgnoreCase))
        {
            return Results.BadRequest(new
            {
                code = "UNSUPPORTED_MEDIA_TYPE",
                message = "Une photo de profil est une image : JPEG ou PNG.",
            });
        }

        return null;
    }
}
