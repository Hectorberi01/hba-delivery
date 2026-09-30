using System.Text.Json;

namespace Hba.Gateway.Endpoints.Relais;

/// <summary>Le résultat d'un dépôt dans Media : l'identifiant, ou le refus.</summary>
public readonly record struct DepotDansMedia(Guid MediaId, IResult? Refus)
{
    public bool EstUnSucces => Refus is null;
}

/// <summary>
/// Relaie un fichier vers le service Media, et rend l'identifiant du média.
///
/// POURQUOI IL REND UN IDENTIFIANT ET NON UNE REPONSE HTTP, contrairement au
/// relais vers Driver. Un dépôt de pièce du dossier livreur est complet en
/// lui-même : Driver écrit le fichier ET la ligne du dossier. Une photo de
/// profil, non — il reste à l'attacher au profil, et c'est Directory qui le
/// fait. L'appelant a donc besoin de l'identifiant pour enchaîner.
///
/// LA PASSERELLE NE DECIDE RIEN AU PASSAGE. Elle renseigne le propriétaire à
/// partir du jeton, mais Media le revérifie de son côté et Directory refuse un
/// média qui ne serait pas une photo de profil appartenant à l'appelant. Deux
/// services vérifient, aucun ne croit la passerelle sur parole.
/// </summary>
public interface IMediaUploadRelay
{
    Task<DepotDansMedia> DeposerAsync(
        HttpRequest requete,
        HttpContext contexte,
        string ownerType,
        string ownerId,
        string kind,
        CancellationToken cancellationToken);
}

public sealed class MediaUploadRelay(HttpClient client, ILogger<MediaUploadRelay> journal)
    : IMediaUploadRelay
{
    public async Task<DepotDansMedia> DeposerAsync(
        HttpRequest requete,
        HttpContext contexte,
        string ownerType,
        string ownerId,
        string kind,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(requete);
        ArgumentNullException.ThrowIfNull(contexte);

        var (fichier, refus) = await RelaisDeDepot.LireAsync(requete, cancellationToken).ConfigureAwait(false);
        if (refus is not null)
        {
            return new DepotDansMedia(Guid.Empty, refus);
        }

        var chemin = "/internal/v1/media"
            + $"?ownerType={Uri.EscapeDataString(ownerType)}"
            + $"&ownerId={Uri.EscapeDataString(ownerId)}"
            + $"&kind={Uri.EscapeDataString(kind)}";

        var reponse = await RelaisDeDepot.EnvoyerAsync(
            client,
            contexte,
            chemin,
            fichier!,
            cancellationToken).ConfigureAwait(false);

        if (!reponse.EstUnSucces)
        {
            // LE CORPS DU SERVICE EST RENDU TEL QUEL : « le fichier fait 9 Mo,
            // la limite est de 5 » est exactement ce que le client doit lire.
            journal.LogWarning("Depot refuse par Media : {Statut}.", reponse.Statut);

            return new DepotDansMedia(
                Guid.Empty,
                Results.Content(reponse.Corps, "application/json", statusCode: reponse.Statut));
        }

        if (!Identifiant(reponse.Corps, out var mediaId))
        {
            // UN SUCCES DONT ON NE SAIT PAS TIRER L'IDENTIFIANT EST UN ECHEC,
            // et il faut le dire : le fichier EST déposé. Le taire rendrait un
            // 200 à un client dont la photo ne s'affichera jamais, et
            // laisserait dans le stockage un objet que rien ne réclame.
            journal.LogError(
                "Media a accepte le depot mais sa reponse ne porte pas d'identifiant lisible.");

            return new DepotDansMedia(
                Guid.Empty,
                Results.Json(
                    new
                    {
                        code = "MEDIA_ID_MISSING",
                        message = "Le fichier a été reçu mais n'a pas pu être rattaché. Réessayez.",
                    },
                    statusCode: StatusCodes.Status502BadGateway));
        }

        return new DepotDansMedia(mediaId, null);
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
