using Hba.BuildingBlocks.Security;
using Hba.Realtime.Api.Diffusion;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Hba.Realtime.Api.Hubs;

/// <summary>
/// Ce que le chauffeur recoit en direct : les offres.
/// </summary>
///
/// <remarks>
/// AUCUNE METHODE A APPELER, ET C'EST TOUTE LA DIFFERENCE AVEC LE HUB DU CLIENT.
///
/// Le client doit DIRE quelle mission il suit, et ce qu'il dit doit etre verifie
/// aupres du service proprietaire. Le chauffeur, lui, n'a rien a demander : ce qu'il
/// recoit est determine par son jeton. Le claim <c>driver_id</c> a ete emis par
/// Identity et valide par ce service ; il n'y a donc rien a verifier aupres de
/// personne, et aucune occasion de se tromper de destinataire.
///
/// L'INSCRIPTION SE FAIT A LA CONNEXION, pas sur appel. Un chauffeur dont
/// l'application vient de se reconnecter doit recevoir l'offre en cours sans avoir a
/// penser a se reabonner — et une offre dure quelques dizaines de secondes, le temps
/// d'un aller-retour de trop.
///
/// CE QUE CE HUB NE REMPLACE PAS : l'appel a <c>GET /offers/current</c> au reveil de
/// l'application. Une offre posee pendant que le telephone dormait n'a pas ete
/// poussee — ce service consomme Kafka au PLUS RECENT, il ne rejoue rien. Le sondage
/// ralenti et la relecture au reveil restent la verite ; ce hub ne fait qu'arriver
/// plus vite quand tout va bien.
/// </remarks>
[Authorize(Roles = HbaRoles.Driver)]
internal sealed class DriverHub(ILogger<DriverHub> logger) : Hub
{
    public override async Task OnConnectedAsync()
    {
        var driverId = Context.User?.FindFirst(HbaClaims.DriverId)?.Value;

        if (string.IsNullOrWhiteSpace(driverId))
        {
            // UN JETON DE CHAUFFEUR SANS driver_id EST UN DEFAUT D'IDENTITY, pas une
            // tentative : le role est la, le claim manque. On refuse la connexion plutot
            // que de la laisser ouverte et muette — un chauffeur qui croit etre branche
            // et ne recoit jamais rien attend sans comprendre.
            logger.LogWarning(
                "Connexion refusee : jeton de chauffeur sans claim {Claim}.",
                HbaClaims.DriverId);

            throw new HubException("Ce jeton de chauffeur ne porte pas driver_id.");
        }

        await Groups
            .AddToGroupAsync(Context.ConnectionId, Groupes.Chauffeur(driverId))
            .ConfigureAwait(false);

        await base.OnConnectedAsync().ConfigureAwait(false);
    }

    // AUCUN OnDisconnectedAsync : SignalR retire une connexion fermee de tous ses
    // groupes. Le faire a la main n'ajouterait rien et laisserait croire que c'est
    // necessaire.
}
