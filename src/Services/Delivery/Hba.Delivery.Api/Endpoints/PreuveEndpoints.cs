using Hba.BuildingBlocks.Application.Abstractions;
using Hba.BuildingBlocks.Application.Messaging;
using Hba.BuildingBlocks.Http;
using Hba.Delivery.Application.Commands.DriverActions;
using Hba.Delivery.Domain.Deliveries;
using Microsoft.AspNetCore.Mvc;

namespace Hba.Delivery.Api.Endpoints;

/// <summary>
/// Le dépôt d'une photo de collecte ou de remise.
/// </summary>
///
/// <remarks>
/// LA SEULE ROUTE HTTP DE CE SERVICE, ET ELLE EXISTE PAR L'ADR 0021 : gRPC porte
/// mal les binaires — un fichier qui traverse les intercepteurs de trace y
/// laisse ce qu'il ne devrait pas, et le typage de bout en bout ne protège rien
/// sur un flux d'octets. Toutes les autres entrées de Delivery restent en gRPC.
///
/// POURQUOI LES OCTETS PASSENT ICI PLUTOT QUE D'ALLER DROIT A MEDIA : c'est la
/// décision du 30 septembre 2026 (point 7, question 4). « Ce livreur est-il
/// affecté à cette course » est une question que SEUL Delivery sait trancher, et
/// le point 27 interdit à Media de se la poser. Delivery autorise donc sur son
/// agrégat, puis dépose chez Media avec un jeton de service. La passerelle, elle,
/// ne détient aucun jeton de service : elle relaie, et ne décide de rien.
///
/// « internal » : jamais exposée directement au téléphone.
///
/// LE PLAFOND ET LES TYPES NE SONT PAS REVERIFIES ICI. Media les porte — cinq
/// mégaoctets, images seulement pour une preuve — et les redire ici créerait deux
/// exemplaires de la même règle, dont l'un finirait par diverger. Seul le plafond
/// de Kestrel est repris, parce qu'il travaille AVANT la lecture du corps : sans
/// lui, un envoi de quatre cents mégaoctets occuperait la mémoire du service le
/// temps que Media le refuse.
/// </remarks>
public static class PreuveEndpoints
{
    /// <summary>
    /// Ce que Kestrel accepte de lire. LARGE EXPRES, et plus large que le
    /// plafond de Media : refuser ici rendrait un 413 nu, sans code métier,
    /// quand Media rend « le fichier fait 9 Mo, la limite est de 5 ».
    /// </summary>
    private const long PlafondDeTransport = (5 * 1024 * 1024) + (64 * 1024);

    public static IEndpointRouteBuilder MapPreuveEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app.MapGroup("/internal/v1/deliveries")
            .RequireAuthorization()
            // SANS CE FILTRE, UN REFUS METIER SORT EN 500 AVEC UN CORPS VIDE :
            // les routes gRPC ont leur intercepteur, celle-ci n'a que lui.
            .AddEndpointFilter<TraduireLesRefus>();

        group.MapPost("/{deliveryId:guid}/proof", async (
            Guid deliveryId,
            [FromQuery] string etape,
            IFormFile fichier,
            IDispatcher dispatcher,
            CancellationToken cancellationToken) =>
        {
            if (!Enum.TryParse<EtapeDeLaPreuve>(etape, ignoreCase: true, out var quelleEtape))
            {
                return Results.BadRequest(new
                {
                    code = "UNKNOWN_PROOF_STEP",
                    message = $"Étape de preuve inconnue : « {etape} ».",
                    attendu = Enum.GetNames<EtapeDeLaPreuve>(),
                });
            }

            if (fichier is null || fichier.Length == 0)
            {
                // CE REFUS-LA EST LE SEUL QUE CETTE ROUTE PRONONCE SUR LE
                // CONTENU, et il ne juge rien : un fichier vide n'a aucun octet
                // a relayer, donc il n'y a rien a demander a Media.
                return Results.BadRequest(new
                {
                    code = "EMPTY_FILE",
                    message = "Fichier vide ou absent.",
                });
            }

            // LE FLUX EST OUVERT ICI ET REFERME ICI. Le gestionnaire le relaie
            // sans le recopier en memoire ; le « await using » garantit qu'il se
            // ferme meme si le depot echoue.
            await using var flux = fichier.OpenReadStream();

            var depot = await dispatcher.SendAsync(
                new AttacherLaPreuveCommand(
                    deliveryId,
                    quelleEtape,
                    flux,
                    fichier.Length,
                    fichier.ContentType),
                cancellationToken).ConfigureAwait(false);

            return Results.Ok(new
            {
                mediaId = depot.MediaId,
                etape = depot.Etape.ToString(),
            });
        })
        .WithMetadata(new RequestSizeLimitAttribute(PlafondDeTransport))
        .DisableAntiforgery();

        return app;
    }
}
