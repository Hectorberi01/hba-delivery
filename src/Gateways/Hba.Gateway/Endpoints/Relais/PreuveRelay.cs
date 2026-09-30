namespace Hba.Gateway.Endpoints.Relais;

/// <summary>
/// Relaie la photo d'une étape vers le service Delivery.
/// </summary>
///
/// <remarks>
/// VERS DELIVERY, ET NON VERS MEDIA : c'est la décision du 30 septembre 2026
/// (point 7, question 4). « Ce livreur est-il affecté à cette course » est une
/// question que seul Delivery sait trancher, et le point 27 interdit à Media de
/// se la poser. Delivery autorise donc sur son agrégat, puis dépose chez Media
/// avec un jeton de service.
///
/// LA PASSERELLE NE DETIENT AUCUN JETON DE SERVICE, ET C'EST LE POINT. Elle
/// reporte celui du livreur et rien d'autre. Lui en donner un ferait d'elle un
/// appelant de confiance pour Media — et la moindre faille dans la passerelle
/// ouvrirait alors tous les médias de la plateforme, pièces d'identité
/// comprises. Ce n'est pas un détail d'implémentation : c'est ce qui distingue
/// ce chemin de celui qui a été écarté.
///
/// IL REND UNE REPONSE COMPLETE, contrairement au relais vers Media qui rend un
/// identifiant. Un dépôt de preuve se suffit à lui-même : Delivery fait écrire
/// le fichier par Media ET rattache l'identifiant à la course. Il n'y a rien à
/// enchaîner, donc rien à extraire du corps.
/// </remarks>
public interface IPreuveRelay
{
    Task<IResult> DeposerAsync(
        HttpRequest requete,
        HttpContext contexte,
        string deliveryId,
        string etape,
        CancellationToken cancellationToken);
}

public sealed class PreuveRelay(HttpClient client, ILogger<PreuveRelay> journal) : IPreuveRelay
{
    public async Task<IResult> DeposerAsync(
        HttpRequest requete,
        HttpContext contexte,
        string deliveryId,
        string etape,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(requete);
        ArgumentNullException.ThrowIfNull(contexte);

        // LE SEUL REFUS QUE LA PASSERELLE PRONONCE, et il ne porte pas sur le
        // contenu : une requête qui n'est pas du multipart, ou dont le fichier
        // est vide, n'a aucun octet à relayer — il n'y a rien à demander au
        // service. Tout le reste (taille, type, état de la course) est jugé plus
        // loin, par ceux dont c'est le métier.
        var (fichier, refus) = await RelaisDeDepot.LireAsync(requete, cancellationToken).ConfigureAwait(false);
        if (refus is not null)
        {
            return refus;
        }

        var chemin = $"/internal/v1/deliveries/{Uri.EscapeDataString(deliveryId)}/proof"
            + $"?etape={Uri.EscapeDataString(etape)}";

        var reponse = await RelaisDeDepot.EnvoyerAsync(
            client,
            contexte,
            chemin,
            fichier!,
            cancellationToken).ConfigureAwait(false);

        if (!reponse.EstUnSucces)
        {
            journal.LogWarning("Depot de preuve refuse par Delivery : {Statut}.", reponse.Statut);
        }

        // LE CORPS DU SERVICE EST RENDU TEL QUEL. « L'étape est trop ancienne »
        // ou « une preuve est déjà jointe » est exactement ce que le livreur doit
        // lire ; le réécrire ici créerait une seconde version de chaque message,
        // qui divergerait de la première.
        return Results.Content(reponse.Corps, "application/json", statusCode: reponse.Statut);
    }
}
