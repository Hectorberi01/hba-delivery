using System.Globalization;
using Hba.Contracts.Common.V1;

namespace Hba.Realtime.Api.Diffusion;

/// <summary>
/// Les noms de groupes SignalR. UN SEUL ENDROIT OU ILS SONT ECRITS.
/// </summary>
///
/// <remarks>
/// POURQUOI CETTE CLASSE EXISTE PLUTOT QUE DES CHAINES EN LIGNE. Un groupe est le
/// SEUL mecanisme d'isolement entre deux clients : si le hub qui ajoute et le
/// diffuseur qui envoie n'ecrivent pas le nom de la meme facon, soit personne ne
/// recoit rien — defaut visible —, soit le message part dans un groupe qui contient
/// quelqu'un d'autre. Le second cas est une fuite de donnees, et il ne se voit pas.
///
/// LA NATURE FAIT PARTIE DU NOM, pas seulement l'identifiant. Une course et une
/// livraison sont deux agregats differents, dans deux services differents, avec deux
/// generateurs d'identifiants independants : rien n'interdit a un identifiant de
/// course de valoir un identifiant de livraison. La probabilite est infime avec des
/// GUID v7, et le cout de s'en proteger est de six caracteres.
/// </remarks>
internal static class Groupes
{
    /// <summary>
    /// Le groupe d'une mission : ce que son client suit.
    /// </summary>
    public static string Mission(ServiceType nature, string missionId)
        => string.Create(
            CultureInfo.InvariantCulture,
            $"mission:{Prefixe(nature)}:{missionId.ToLowerInvariant()}");

    /// <summary>
    /// Le groupe d'un chauffeur : ce qu'il recoit, et lui seul.
    /// </summary>
    ///
    /// <remarks>
    /// UN GROUPE A UN SEUL MEMBRE, EN GENERAL, ET POURTANT PAS Clients.User(). La
    /// difference tient a ce qui identifie : <c>Clients.User</c> part du
    /// <c>NameIdentifier</c> du jeton, c'est-a-dire du COMPTE ; ici on veut le
    /// CHAUFFEUR, qui est un claim distinct (driver_id). Les faire coincider
    /// marcherait aujourd'hui et se romprait le jour ou un compte porterait deux
    /// roles.
    ///
    /// PLUSIEURS MEMBRES EST UN CAS NORMAL : le telephone du chauffeur et sa tablette,
    /// ou simplement une reconnexion dont l'ancienne connexion n'est pas encore
    /// expiree. Une offre doit arriver sur les deux.
    /// </remarks>
    public static string Chauffeur(string driverId)
        => string.Create(CultureInfo.InvariantCulture, $"driver:{driverId.ToLowerInvariant()}");

    /// <summary>
    /// Le groupe de l'exploitation : la carte en direct.
    ///
    /// UN SEUL GROUPE POUR TOUT LE BACK-OFFICE, sans decoupage par role. Ce qui y
    /// passe — les positions de tout le monde — est deja reserve a admin, ops, support
    /// et finance par la politique du hub ; decouper plus finement demanderait de
    /// decider ce que support voit de moins qu'ops, et personne ne l'a tranche.
    /// </summary>
    public const string Exploitation = "ops";

    /// <summary>
    /// L'inverse de <see cref="Mission"/> : retrouve la nature et l'identifiant.
    /// </summary>
    ///
    /// <remarks>
    /// POURQUOI IL FAUT DECOMPOSER PLUTOT QUE DE TRANSPORTER LES DEUX. L'annuaire des
    /// missions range des NOMS DE GROUPES, parce que c'est ce dont le diffuseur a besoin
    /// pour envoyer. Mais la trame destinee au client, elle, porte la nature et
    /// l'identifiant separes — une application n'a pas a analyser un nom de groupe
    /// SignalR pour savoir quelle mission elle regarde.
    ///
    /// RANGER LES DEUX FORMES DANS REDIS AURAIT DOUBLE L'ECRITURE pour une information
    /// qui se deduit ; les transporter dans la trame sous forme de nom de groupe aurait
    /// fait du detail interne de ce service un element du contrat des applications.
    /// Decomposer ici est le moindre des trois maux.
    ///
    /// UN NOM QU'ON NE RECONNAIT PAS REND DEUX CHAINES VIDES, et l'appelant doit le
    /// tester. Cela n'arrive que si l'annuaire contient une entree ecrite par une version
    /// anterieure du format — auquel cas ne rien envoyer est le bon comportement.
    /// </remarks>
    public static (string Nature, string MissionId) Decomposer(string groupe)
    {
        if (string.IsNullOrWhiteSpace(groupe))
        {
            return (string.Empty, string.Empty);
        }

        var morceaux = groupe.Split(':');

        if (morceaux.Length != 3 || morceaux[0] != "mission")
        {
            return (string.Empty, string.Empty);
        }

        var nature = morceaux[1] switch
        {
            "ride" => "course",
            "delivery" => "livraison",
            _ => string.Empty,
        };

        return nature.Length == 0 ? (string.Empty, string.Empty) : (nature, morceaux[2]);
    }

    private static string Prefixe(ServiceType nature) => nature switch
    {
        ServiceType.Ride => "ride",
        ServiceType.Delivery => "delivery",

        // UNE NATURE NON PRECISEE NE DEVIENT PAS UN COLIS ICI, contrairement a ce que
        // fait Pricing. La raison est la nature du defaut : un devis mal attribue
        // coute un prix faux, un GROUPE mal attribue envoie la position d'un chauffeur
        // au client de quelqu'un d'autre. Le prefixe « unknown » ne correspond a aucun
        // groupe reel, donc l'abonnement ne recevra rien — ce qui est le bon echec.
        _ => "unknown",
    };
}
