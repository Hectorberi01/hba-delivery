using Hba.BuildingBlocks.Security;
using Hba.Realtime.Api.Diffusion;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Hba.Realtime.Api.Hubs;

/// <summary>
/// La carte de l'exploitation, en direct.
/// </summary>
///
/// <remarks>
/// LE FLUX LE PLUS BAVARD POUR LE PUBLIC LE PLUS PETIT, et c'est assume : tous les
/// battements de tous les chauffeurs en ligne, vers quelques ecrans. A cent
/// chauffeurs et une cadence de vingt secondes, cela fait cinq messages par seconde
/// — rien du tout.
///
/// C'EST LE SEUL HUB QUI VOIT LE CAP, LA VITESSE ET LA PRECISION. Le client n'en
/// recoit aucun (point 37) : « ou est-il » suffit a decider d'attendre, et la vitesse
/// d'un homme au travail est une donnee de surveillance. L'exploitation, elle, en a
/// besoin pour comprendre pourquoi une course traine — un chauffeur immobile depuis
/// dix minutes et un chauffeur qui roule ne demandent pas le meme coup de telephone.
///
/// RESERVE A admin, ops, support ET finance par la politique, et c'est la meme
/// population que la carte en sondage. Un jeton de service, qui ne porte aucun role,
/// est refuse : aucun service n'a besoin de regarder une carte.
///
/// IL NE NOMME PERSONNE. Le flux porte des identifiants et des points ; la console
/// garde les noms qu'elle a deja charges par la route REST. Les renvoyer a chaque
/// battement aurait fait passer un nom et un telephone cinq fois par seconde sur le
/// reseau, pour une information qui ne change jamais.
/// </remarks>
[Authorize(Policy = HbaPolicies.BackOffice)]
internal sealed class OpsHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, Groupes.Exploitation).ConfigureAwait(false);
        await base.OnConnectedAsync().ConfigureAwait(false);
    }
}
