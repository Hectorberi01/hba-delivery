using System.Net.Http.Headers;
using System.Text.Json;
using Hba.BuildingBlocks.Domain;
using Hba.BuildingBlocks.Grpc.ServiceAuth;
using Hba.Delivery.Application.Ports;
using Microsoft.Extensions.Logging;

namespace Hba.Delivery.Infrastructure.Clients;

/// <summary>
/// Le port IDepotDePreuve, branché sur la route HTTP du service Media.
/// </summary>
///
/// <remarks>
/// EN HTTP, PAS EN gRPC, et c'est l'ADR 0021 : un fichier qui traverse les
/// intercepteurs de trace y laisse ce qu'il ne devrait pas, et le typage de bout
/// en bout ne protège rien sur un flux d'octets. Media expose une route HTTP
/// parce que c'est son métier ; l'appeler ainsi n'est pas une exception.
///
/// LE PORT 8080, PAS 8081 : la surface HTTP du service, pas sa surface gRPC. Se
/// tromper donne un « HTTP_1_1_REQUIRED » qui ne désigne pas sa cause.
/// </remarks>
public sealed class DepotDePreuveHttp(
    HttpClient client,
    IServiceTokenProvider jetons,
    ILogger<DepotDePreuveHttp> journal) : IDepotDePreuve
{
    public async Task<Guid> DeposerAsync(
        Guid deliveryId,
        Stream contenu,
        long tailleOctets,
        string typeDeContenu,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(contenu);

        // « GetTokenAsync » ET NON « AuthorizationAsync », ET C'EST DELIBERE.
        //
        // AuthorizationAsync ne pose rien quand l'appel descend d'une requete
        // utilisateur : sa regle est « un utilisateur passe avant le service »,
        // parce qu'un jeton de service contournerait les regles au lieu de les
        // satisfaire. Ici cette regle ne peut pas s'appliquer — une course n'a
        // pas de compte, donc AUCUN jeton d'utilisateur ne peut deposer sous
        // elle, pas meme celui du livreur affecte. C'est exactement ce que le
        // point 7 a tranche : Delivery a deja verifie sur son agregat que ce
        // livreur est le bon, et c'est lui qui depose a sa place.
        //
        // CE JETON N'OUVRE QU'UNE PORTE, et elle est etroite : Media n'accepte
        // du role « service » qu'une preuve, sous une course. Rien d'autre.
        var jeton = await jetons.GetTokenAsync(cancellationToken).ConfigureAwait(false);

        var chemin = "/internal/v1/media"
            + "?ownerType=Delivery"
            + $"&ownerId={Uri.EscapeDataString(deliveryId.ToString())}"
            + "&kind=DeliveryProof";

        using var requete = new HttpRequestMessage(HttpMethod.Post, chemin);
        requete.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jeton);

        // LE FLUX EST RELAYE, PAS RECOPIE EN MEMOIRE. Un ReadAsByteArrayAsync
        // ferait tenir cinq megaoctets par envoi simultane dans le tas du
        // service, et au-dela de 85 Ko cela part directement dans le tas des
        // grands objets, qui ne se compacte pas.
        using var corps = new MultipartFormDataContent();
        using var partie = new StreamContent(contenu);
        partie.Headers.ContentType = MediaTypeHeaderValue.Parse(
            string.IsNullOrWhiteSpace(typeDeContenu) ? "application/octet-stream" : typeDeContenu);
        partie.Headers.ContentLength = tailleOctets;
        corps.Add(partie, "fichier", "preuve");
        requete.Content = corps;

        using var reponse = await client
            .SendAsync(requete, HttpCompletionOption.ResponseContentRead, cancellationToken)
            .ConfigureAwait(false);

        var texte = await reponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!reponse.IsSuccessStatusCode)
        {
            // LE REFUS DE MEDIA EST TRADUIT, PAS RECOPIE. Son corps porte ses
            // propres codes — « FILE_TOO_LARGE », « UNSUPPORTED_MEDIA_TYPE » —
            // que le livreur doit lire ; mais le faire remonter tel quel
            // exposerait la forme interne d'un autre service dans une reponse
            // que la passerelle rend au telephone.
            journal.LogWarning(
                "Media a refuse la preuve de la course {DeliveryId} ({Statut}).",
                deliveryId,
                (int)reponse.StatusCode);

            throw new DomainException("PROOF_UPLOAD_REFUSED", Explication(texte));
        }

        if (!Identifiant(texte, out var mediaId))
        {
            // UN SUCCES DONT ON NE SAIT PAS TIRER L'IDENTIFIANT EST UN ECHEC,
            // et il faut le dire : le fichier EST depose. Le taire rattacherait
            // a la course un Guid vide, et laisserait dans le stockage un objet
            // que la course ne reclame pas — que la purge a trente jours
            // balaiera, elle, parce qu'il est dans l'inventaire.
            journal.LogError(
                "Media a accepte la preuve de la course {DeliveryId} mais sa reponse ne porte "
                + "pas d'identifiant lisible.",
                deliveryId);

            throw new DomainException(
                "PROOF_UPLOAD_REFUSED",
                "La photo a été reçue mais n'a pas pu être rattachée. Réessayez.");
        }

        return mediaId;
    }

    /// <summary>Le message de Media s'il en porte un, sinon une phrase à nous.</summary>
    private static string Explication(string corps)
    {
        try
        {
            using var document = JsonDocument.Parse(corps);

            if (document.RootElement.TryGetProperty("message", out var message)
                && message.GetString() is { Length: > 0 } texte)
            {
                return texte;
            }
        }
        catch (JsonException)
        {
            // Un corps illisible n'est pas une panne de plus : on tombe sur la
            // phrase generique ci-dessous.
        }

        return "La photo n'a pas pu être enregistrée.";
    }

    private static bool Identifiant(string corps, out Guid mediaId)
    {
        mediaId = Guid.Empty;

        try
        {
            using var document = JsonDocument.Parse(corps);

            return document.RootElement.TryGetProperty("id", out var propriete)
                   && Guid.TryParse(propriete.GetString(), out mediaId);
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
