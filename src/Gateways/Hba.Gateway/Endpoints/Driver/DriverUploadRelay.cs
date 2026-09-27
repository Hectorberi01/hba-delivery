using System.Net.Http.Headers;
using Hba.BuildingBlocks.Security;

namespace Hba.Gateway.Endpoints.Driver;

/// <summary>
/// Relaie un envoi de fichier vers le service Driver.
///
/// LA PASSERELLE NE REGARDE PAS LE CONTENU. Elle ne connaît ni le stockage,
/// ni la convention de clés, ni les formats acceptés : c'est Driver qui
/// valide, écrit et enregistre ensemble (ADR 0021). Tout ce que fait cette
/// classe, c'est porter les octets et le jeton.
/// </summary>
public interface IDriverUploadRelay
{
    Task<IResult> RelayerAsync(
        HttpRequest requete,
        HttpContext contexte,
        string cheminRelatif,
        CancellationToken cancellationToken);
}

public sealed class DriverUploadRelay(HttpClient client, ILogger<DriverUploadRelay> journal)
    : IDriverUploadRelay
{
    public async Task<IResult> RelayerAsync(
        HttpRequest requete,
        HttpContext contexte,
        string cheminRelatif,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(requete);
        ArgumentNullException.ThrowIfNull(contexte);

        if (!requete.HasFormContentType)
        {
            return Results.BadRequest(new
            {
                code = "EXPECTED_MULTIPART",
                message = "Envoyez le fichier en multipart/form-data, champ « fichier ».",
            });
        }

        var formulaire = await requete.ReadFormAsync(cancellationToken).ConfigureAwait(false);
        var fichier = formulaire.Files["fichier"] ?? formulaire.Files.FirstOrDefault();

        if (fichier is null || fichier.Length == 0)
        {
            return Results.BadRequest(new { code = "EMPTY_DOCUMENT", message = "Fichier vide ou absent." });
        }

        var driverId = DriverEndpoints.DriverIdOf(contexte);

        // LE FLUX EST RELAYE, PAS RECOPIE EN MEMOIRE. Un ReadAsByteArrayAsync
        // ferait tenir cinq megaoctets par envoi simultane dans le tas de la
        // passerelle, et au-dela de 85 Ko cela part directement dans le tas
        // des grands objets, qui ne se compacte pas.
        await using var flux = fichier.OpenReadStream();

        using var contenu = new MultipartFormDataContent();
        using var partie = new StreamContent(flux);
        partie.Headers.ContentType = MediaTypeHeaderValue.Parse(
            string.IsNullOrWhiteSpace(fichier.ContentType)
                ? "application/octet-stream"
                : fichier.ContentType);

        contenu.Add(partie, "fichier", fichier.FileName);

        using var envoi = new HttpRequestMessage(
            HttpMethod.Post,
            $"/internal/v1/drivers/{driverId}/{cheminRelatif}")
        {
            Content = contenu,
        };

        // LE JETON DU LIVREUR EST REPORTE TEL QUEL. Le service revérifie
        // l'autorisation dans son handler (ADR 0007) : la passerelle qui
        // affirmerait « c'est bien lui » ne serait crue par personne.
        var autorisation = contexte.Request.Headers.Authorization.FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(autorisation))
        {
            envoi.Headers.TryAddWithoutValidation("Authorization", autorisation);
        }

        var correlation = contexte.Request.Headers["hba-correlation-id"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(correlation))
        {
            envoi.Headers.TryAddWithoutValidation("hba-correlation-id", correlation);
        }

        using var reponse = await client
            .SendAsync(envoi, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        var corps = await reponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!reponse.IsSuccessStatusCode)
        {
            // LE CODE ET LE CORPS DU SERVICE SONT RENDUS TELS QUELS. Les
            // remplacer par un 500 générique effacerait « votre pièce dépasse
            // 5 Mo », qui est exactement ce que le livreur doit lire.
            journal.LogWarning(
                "Depot de piece refuse par Driver : {Statut} sur {Chemin}.",
                (int)reponse.StatusCode,
                cheminRelatif);

            return Results.Content(corps, "application/json", statusCode: (int)reponse.StatusCode);
        }

        return Results.Content(corps, "application/json");
    }
}
