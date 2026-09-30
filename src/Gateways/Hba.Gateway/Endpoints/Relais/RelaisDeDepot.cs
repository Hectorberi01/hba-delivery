using System.Net.Http.Headers;

namespace Hba.Gateway.Endpoints.Relais;

/// <summary>Ce qu'un service a répondu à un dépôt de fichier.</summary>
public readonly record struct ReponseDeDepot(int Statut, string Corps)
{
    public bool EstUnSucces => Statut is >= 200 and < 300;
}

/// <summary>
/// La mécanique commune à tous les relais d'octets de la passerelle.
///
/// ELLE EXISTE PARCE QU'IL Y EN A DEUX. Tant que le dépôt des pièces du
/// dossier livreur était seul, sa mécanique pouvait vivre chez lui. La photo de
/// profil du client en fait un second, vers un autre service — et deux copies
/// de la même subtilité (ne pas recopier le flux en mémoire, reporter le jeton
/// tel quel, rendre le corps du service sans le réécrire) sont deux copies qui
/// divergeront, l'une corrigée et l'autre non.
///
/// LA PASSERELLE NE REGARDE PAS LE CONTENU. Elle ne connaît ni le stockage, ni
/// les formats acceptés, ni les plafonds : le service valide, écrit et
/// enregistre. Tout ce que fait ce fichier, c'est porter les octets, le jeton
/// et la corrélation.
/// </summary>
internal static class RelaisDeDepot
{
    /// <summary>
    /// Le fichier reçu, ou le refus à rendre tel quel.
    /// </summary>
    ///
    /// <remarks>
    /// CE REFUS-LA EST LE SEUL QUE LA PASSERELLE PRONONCE, et il ne porte pas
    /// sur le contenu : une requête qui n'est pas du multipart n'a aucun octet
    /// à relayer, donc il n'y a rien à demander au service.
    /// </remarks>
    public static async Task<(IFormFile? Fichier, IResult? Refus)> LireAsync(
        HttpRequest requete,
        CancellationToken cancellationToken)
    {
        if (!requete.HasFormContentType)
        {
            return (null, Results.BadRequest(new
            {
                code = "EXPECTED_MULTIPART",
                message = "Envoyez le fichier en multipart/form-data, champ « fichier ».",
            }));
        }

        var formulaire = await requete.ReadFormAsync(cancellationToken).ConfigureAwait(false);
        var fichier = formulaire.Files["fichier"] ?? formulaire.Files.FirstOrDefault();

        if (fichier is null || fichier.Length == 0)
        {
            return (null, Results.BadRequest(new
            {
                code = "EMPTY_FILE",
                message = "Fichier vide ou absent.",
            }));
        }

        return (fichier, null);
    }

    public static async Task<ReponseDeDepot> EnvoyerAsync(
        HttpClient client,
        HttpContext contexte,
        string chemin,
        IFormFile fichier,
        CancellationToken cancellationToken)
    {
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

        using var envoi = new HttpRequestMessage(HttpMethod.Post, chemin) { Content = contenu };

        // LE JETON DE L'APPELANT EST REPORTE TEL QUEL. Le service revérifie
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

        return new ReponseDeDepot((int)reponse.StatusCode, corps);
    }
}
