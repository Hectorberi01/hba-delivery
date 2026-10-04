using Grpc.Core;
using Hba.BuildingBlocks.Security;
using Hba.Contracts.Common.V1;
using Hba.Contracts.Delivery.V1;
using Hba.Contracts.Ride.V1;
using Hba.Realtime.Api.Diffusion;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Hba.Realtime.Api.Hubs;

/// <summary>
/// Ce que le client suit en direct : l'etat de sa mission, et la position du
/// chauffeur qui vient.
/// </summary>
///
/// <remarks>
/// LE HUB NE DECIDE PAS SI LA MISSION EST LA SIENNE — IL LE DEMANDE.
///
/// C'est le choix central de ce fichier. Un client s'abonne en donnant un
/// identifiant de mission ; cet identifiant vient de LUI, donc il ne prouve rien.
/// Deux facons de verifier :
///
/// — reecrire ici la regle « cette mission appartient-elle a cet appelant » ;
/// — appeler le service PROPRIETAIRE avec le jeton du client, et regarder s'il
///   repond.
///
/// La seconde est la seule tenable. La premiere ferait exister la regle en deux
/// exemplaires, dont l'un ne serait jamais teste, et le jour ou Ride ajouterait un
/// cas — une course commandee pour un proche, un acces du support — ce hub
/// continuerait d'appliquer l'ancienne. Or ici, une regle qui diverge ne rend pas un
/// mauvais chiffre : elle envoie la position d'un homme a l'ecran de quelqu'un
/// d'autre.
///
/// RIDE ET DELIVERY REFUSENT DEJA, et c'est tout ce dont on a besoin :
/// <c>GetRide</c> et <c>GetDelivery</c> verifient le peril de l'appelant cote
/// service (ADR 0007). Un NOT_FOUND ou un PERMISSION_DENIED vaut « pas a toi », et
/// le hub n'a pas a savoir lequel des deux.
///
/// LA POSITION N'EST PAS DIFFUSEE PAR CE FICHIER. Il inscrit l'abonne dans le groupe
/// de la mission ; c'est <see cref="Diffusion.DiffuseurDePositions"/> qui y envoie
/// les battements, et lui seul connait la borne du point 37 — position seule, ni
/// cap, ni vitesse, ni precision.
///
/// CE QUI ARRIVE A LA CLOTURE. L'abonnement n'est pas coupe : le client doit encore
/// recevoir l'etat « terminee ». C'est l'ANNUAIRE qui oublie la mission, donc les
/// positions cessent — exactement la borne que le point 37 exige. Le groupe, lui,
/// se vide quand le client ferme son ecran.
/// </remarks>
[Authorize(Roles = HbaRoles.Customer)]
internal sealed class CustomerHub(
    RideService.RideServiceClient courses,
    DeliveryService.DeliveryServiceClient livraisons,
    AnnuaireDesMissions annuaire,
    Presences presences,
    ILogger<CustomerHub> logger) : Hub
{
    /// <summary>
    /// Nom de la methode que le client appelle pour suivre une mission.
    ///
    /// LES NOMS SONT EN FRANCAIS COMME LE RESTE DU DEPOT, et ils font partie du
    /// contrat : les applications les ecrivent en dur. Les renommer casse les clients
    /// deployes exactement comme renommer une route REST.
    /// </summary>
    public async Task SuivreLaMission(string nature, string missionId)
    {
        var type = Nature(nature);

        if (type == ServiceType.Unspecified || string.IsNullOrWhiteSpace(missionId))
        {
            throw new HubException("Il faut une nature — « course » ou « livraison » — et un identifiant.");
        }

        var entetes = JetonDeConnexion.PourAppel(Context);

        // LE DROIT D'ABORD, LE GROUPE ENSUITE. Inscrire puis verifier laisserait une
        // fenetre — courte, mais reelle — ou un battement partirait vers quelqu'un qui
        // n'y a pas droit.
        var chauffeur = await ChauffeurAffecteAsync(type, missionId, entetes).ConfigureAwait(false);

        if (chauffeur is null)
        {
            // ON NE DIT PAS LAQUELLE DES DEUX RAISONS. « Elle n'est pas a toi » et
            // « elle n'existe pas » se repondent du meme refus : distinguer les deux
            // permettrait de deviner quels identifiants existent en essayant.
            throw new HubException("Cette mission n'est pas accessible.");
        }

        var groupe = Groupes.Mission(type, missionId);

        await Groups.AddToGroupAsync(Context.ConnectionId, groupe).ConfigureAwait(false);

        // LE GROUPE SIGNALR ET LA TABLE DES PRESENCES SONT TENUS EN PARALLELE, et il
        // faut les deux. Le groupe sert aux diffuseurs Kafka, qui envoient a un groupe et
        // laissent le backplane traverser les repliques. La table sert au diffuseur de
        // positions, qui envoie aux CONNEXIONS locales pour ne pas dedoubler un battement
        // que le pub/sub a deja livre partout. Oublier cette ligne ne casse donc pas
        // l'etat de la mission — seulement le point qui bouge, et seulement pour cet
        // abonne. C'est exactement le genre de defaut qui ne se voit pas en recette.
        presences.Suivre(Context.ConnectionId, groupe);

        // L'ANNUAIRE EST REPARE ICI, ET C'EST LE CHEMIN QUI SAUVE UN REDEMARRAGE.
        //
        // L'association mission → chauffeur est normalement ecrite par l'evenement
        // d'affectation. Mais pour une mission DEJA affectee, cet evenement est passe :
        // un hub qui redemarre ne le reverra jamais, et le client reconnecte
        // n'obtiendrait plus aucune position jusqu'a la fin de sa course. On reinscrit
        // donc ce que le service proprietaire vient de nous dire.
        if (chauffeur.Length > 0)
        {
            await annuaire.AffecterAsync(type, missionId, chauffeur).ConfigureAwait(false);
        }

        logger.LogDebug("Abonnement de {Connexion} au groupe {Groupe}.", Context.ConnectionId, groupe);
    }

    /// <summary>
    /// Le client ferme son ecran de suivi.
    ///
    /// CE N'EST PAS INDISPENSABLE — la deconnexion vide le groupe toute seule — mais
    /// une application qui passe d'une mission a l'autre sans se deconnecter resterait
    /// sinon abonnee aux deux, et recevrait des positions qu'elle n'affiche pas.
    /// </summary>
    public async Task CesserDeSuivre(string nature, string missionId)
    {
        var type = Nature(nature);

        if (type == ServiceType.Unspecified || string.IsNullOrWhiteSpace(missionId))
        {
            return;
        }

        var groupe = Groupes.Mission(type, missionId);

        await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupe).ConfigureAwait(false);
        presences.NePlusSuivre(Context.ConnectionId, groupe);
    }

    /// <summary>
    /// SignalR retire la connexion de ses groupes tout seul ; la table des presences,
    /// elle, est a nous.
    /// </summary>
    ///
    /// <remarks>
    /// APPELE MEME QUAND LE RESEAU TOMBE SANS FERMETURE PROPRE — SignalR le declenche au
    /// bout de son delai de maintien. C'est ce qui garantit qu'une application fermee
    /// brutalement, un telephone eteint ou un tunnel ne laissent pas d'entree derriere
    /// eux. Sans cela, la table grossirait d'une entree par ecran ouvert dans la journee,
    /// et le diffuseur enverrait des battements a des connexions mortes.
    /// </remarks>
    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        presences.OublierLaConnexion(Context.ConnectionId);
        await base.OnDisconnectedAsync(exception).ConfigureAwait(false);
    }

    /// <summary>
    /// Demande au service proprietaire si la mission est accessible, et qui y est
    /// affecte. Rend null si elle ne l'est pas, une chaine vide si aucun chauffeur ne
    /// l'est encore.
    /// </summary>
    ///
    /// <remarks>
    /// LA CHAINE VIDE N'EST PAS NULL, ET LA DIFFERENCE PORTE LE SENS. Null veut dire
    /// « tu n'y as pas droit » et refuse l'abonnement ; vide veut dire « elle est a toi,
    /// personne n'est encore affecte » et l'accorde — le client doit pouvoir suivre une
    /// course qui cherche encore un chauffeur, c'est meme le moment ou il regarde le
    /// plus son ecran.
    /// </remarks>
    private async Task<string?> ChauffeurAffecteAsync(ServiceType nature, string missionId, Metadata entetes)
    {
        try
        {
            if (nature == ServiceType.Ride)
            {
                var course = await courses
                    .GetRideAsync(new GetRideRequest { RideId = missionId }, headers: entetes)
                    .ConfigureAwait(false);

                // DEUX CHAMPS PORTENT LE CHAUFFEUR, ET ON PREFERE L'INSTANTANE. « Driver »
                // est la photographie prise a l'affectation — nom, vehicule, plaque — et
                // « DriverId » le champ brut, plus ancien. Les deux disent le meme
                // identifiant ; prendre l'instantane d'abord fait echouer vite le jour ou
                // l'un des deux cesserait d'etre rempli.
                return course.Driver is { DriverId.Length: > 0 }
                    ? course.Driver.DriverId
                    : course.DriverId;
            }

            var livraison = await livraisons
                .GetDeliveryAsync(new GetDeliveryRequest { DeliveryId = missionId }, headers: entetes)
                .ConfigureAwait(false);

            return livraison.Driver is { DriverId.Length: > 0 }
                ? livraison.Driver.DriverId
                : string.Empty;
        }
        catch (RpcException ex) when (ex.StatusCode is StatusCode.NotFound
            or StatusCode.PermissionDenied
            or StatusCode.Unauthenticated
            or StatusCode.InvalidArgument)
        {
            logger.LogInformation(
                "Abonnement refuse pour la mission {Nature}/{Mission} : {Statut}.",
                nature,
                missionId,
                ex.StatusCode);

            return null;
        }
    }

    /// <summary>
    /// La nature, ecrite en toutes lettres par l'application.
    ///
    /// AUCUN REPLI SUR LE COLIS ICI, contrairement a ce que fait Pricing. Un devis mal
    /// attribue coute un prix faux ; un groupe mal attribue envoie la position d'un
    /// chauffeur a l'ecran d'un inconnu. Un nom qu'on ne reconnait pas est donc refuse.
    /// </summary>
    private static ServiceType Nature(string? nom) => nom?.Trim().ToLowerInvariant() switch
    {
        "course" or "ride" => ServiceType.Ride,
        "livraison" or "colis" or "delivery" => ServiceType.Delivery,
        _ => ServiceType.Unspecified,
    };
}
