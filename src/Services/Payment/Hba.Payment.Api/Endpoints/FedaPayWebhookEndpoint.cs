using Hba.BuildingBlocks.Application.Messaging;
using Hba.Payment.Application.Common.Interfaces;
using Hba.Payment.Application.Features.Payments.Commands;

namespace Hba.Payment.Api.Endpoints;

/// <summary>
/// Point d'entree des notifications du fournisseur.
///
/// C'EST LA SEULE PORTE PAR LAQUELLE UN PAIEMENT DEVIENT REEL. Le contrat le
/// dit : « Seule source de verite du paiement : le webhook FedaPay verifie.
/// Delivery ne passe en PAID que sur cet evenement. » Le retour du client sur
/// l'ecran « merci » ne prouve rien et n'est branche sur rien.
/// </summary>
internal static class FedaPayWebhookEndpoint
{
    /// <summary>
    /// En-tete de signature. Le nom vient du fournisseur ; la verification est
    /// dans <c>FedaPayWebhookVerifier</c>.
    /// </summary>
    private const string SignatureHeader = "X-FEDAPAY-SIGNATURE";

    /// <summary>
    /// Une notification signee fait quelques kilo-octets. Borner la lecture
    /// evite qu'un corps arbitrairement long ne soit avale puis hache.
    /// </summary>
    private const int MaxBodyBytes = 64 * 1024;

    public static IEndpointRouteBuilder MapFedaPayWebhook(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapPost("/webhooks/fedapay", async (
            HttpRequest request,
            IWebhookVerifier verifier,
            IDispatcher dispatcher,
            ILoggerFactory loggerFactory,
            CancellationToken cancellationToken) =>
        {
            var logger = loggerFactory.CreateLogger("FedaPayWebhook");

            if (request.ContentLength > MaxBodyBytes)
            {
                return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
            }

            // LE CORPS BRUT, PAS UN OBJET DESERIALISE. La signature porte sur
            // les octets recus : reserialiser le JSON, ne serait-ce que pour
            // reordonner une cle, invaliderait toutes les notifications.
            using var reader = new StreamReader(request.Body);
            var rawBody = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);

            var reference = verifier.ReadTransactionReference(
                rawBody,
                request.Headers[SignatureHeader].ToString());

            if (reference is null)
            {
                // 400 ET NON 401 : l'appelant n'a pas a apprendre, du code de
                // statut, s'il s'est trompe de secret ou de format.
                logger.LogWarning("Notification de paiement refusee : signature absente ou invalide.");
                return Results.BadRequest();
            }

            var outcome = await dispatcher
                .SendAsync(new ApplyProviderOutcomeCommand(reference), cancellationToken)
                .ConfigureAwait(false);

            // 200 DANS TOUS LES CAS OU LA SIGNATURE TIENT, y compris pour une
            // reference inconnue. Le fournisseur rejoue jusqu'a neuf fois ce
            // qu'il n'a pas vu acquitte ; renvoyer une erreur sur une
            // notification que nous avons bien traitee — ou qui ne nous
            // concerne pas — provoquerait huit rejeux inutiles.
            logger.LogInformation(
                "Notification de paiement traitee pour {Reference} : {Outcome}.",
                reference,
                outcome);

            return Results.Ok();
        })
        .AllowAnonymous()
        .WithName("FedaPayWebhook");

        return endpoints;
    }
}
