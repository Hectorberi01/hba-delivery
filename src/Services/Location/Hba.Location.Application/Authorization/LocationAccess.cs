using Hba.BuildingBlocks.Application.Abstractions;
using Hba.BuildingBlocks.Domain;
using Hba.BuildingBlocks.Security;

namespace Hba.Location.Application.Authorization;

/// <summary>
/// Qui a le droit de demander ou se tient quelqu'un.
/// </summary>
///
/// <remarks>
/// VERIFIE ICI, PAS A LA PASSERELLE (ADR 0007). La position d'une personne est la
/// donnee la plus sensible du systeme apres les pieces d'identite : elle dit ou
/// quelqu'un se tient MAINTENANT. Le service ne fait donc pas confiance a l'appelant
/// pour avoir deja verifie.
///
/// TROIS LECTURES, TROIS POPULATIONS DIFFERENTES, et l'ecart entre elles est tout le
/// propos de ce fichier :
///
/// — LA CARTE COMPLETE est reservee au back-office. Elle rend TOUT LE MONDE, nomme
///   personne, et se lit comme un tableau de surveillance. Un jeton de service, qui
///   ne porte aucun role, est refuse par la meme ligne : le dispatch cherche par
///   proximite, il ne balaie pas la ville.
///
/// — LA RECHERCHE DE PROXIMITE est ouverte plus largement, parce qu'elle repond a une
///   question bornee : « y a-t-il quelqu'un pres d'ICI ». Le dispatch la pose avec un
///   jeton de service ; la passerelle la pose avec le jeton du CLIENT, pour la carte
///   de l'application.
///
/// — LA POSITION D'UN CHAUFFEUR NOMME demande un jeton de service ou le back-office.
///   Elle designe une personne, et un client qui pourrait l'appeler suivrait un
///   chauffeur precis d'un jour sur l'autre.
///
/// CE QUE CE FICHIER NE PROTEGE PAS, ET QU'IL FAUT SAVOIR. La recherche de proximite
/// rend des IDENTIFIANTS de chauffeur, parce que le dispatch en a besoin pour ouvrir
/// une vague. C'est la PASSERELLE qui les fait disparaitre avant d'atteindre le
/// client (point 24, et le commentaire de ClientNearbyEndpoints l'explique en
/// detail) ; ce service, lui, les rend a tout appelant autorise. Resserrer cela
/// demanderait que la passerelle porte un jeton de SERVICE pour cet appel au lieu de
/// reporter celui du client — c'est faisable, ce n'est pas tranche, et l'exposition
/// est aujourd'hui exactement celle qui existait quand cette recherche vivait dans
/// Driver.
/// </remarks>
public static class LocationAccess
{
    /// <summary>
    /// Ecrire sa propre position, ou celle d'un autre quand on est de l'exploitation.
    ///
    /// LE BACK-OFFICE PEUT ECRIRE, ET CE N'EST PAS UNE PORTE DEROBEE : c'est admin et
    /// ops uniquement, et cela sert au support — un chauffeur dont le telephone ne
    /// remonte plus rien, qu'on repose a la main sur la carte le temps de comprendre.
    /// </summary>
    public static void EnsureCanWrite(ICallerContext caller, Guid driverId)
    {
        ArgumentNullException.ThrowIfNull(caller);

        if (caller.IsInRole(HbaRoles.Admin) || caller.IsInRole(HbaRoles.Ops))
        {
            return;
        }

        var soi = caller.DriverId ?? caller.SubjectId;

        if (!string.Equals(soi, driverId.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            throw new ForbiddenException("Un chauffeur ne declare que sa propre position.");
        }
    }

    /// <summary>La carte complete : back-office seulement.</summary>
    public static void EnsureCanSeeEveryone(ICallerContext caller)
    {
        ArgumentNullException.ThrowIfNull(caller);

        if (!caller.Roles.Overlaps(HbaRoles.BackOffice))
        {
            throw new ForbiddenException("La carte des chauffeurs est reservee au back-office.");
        }
    }

    /// <summary>
    /// La position d'un chauffeur nomme : jeton de service, ou back-office.
    /// </summary>
    ///
    /// <remarks>
    /// UN JETON DE SERVICE NE PORTE AUCUN ROLE, d'ou le test sur l'absence de role et
    /// non sur une liste. C'est le dispatch qui appelle, pour une affectation manuelle
    /// que l'exploitation a demandee — et son autorisation a elle a deja ete verifiee
    /// la-bas, dans le handler qui pose l'offre (ADR 0018).
    /// </remarks>
    public static void EnsureCanLocateOne(ICallerContext caller)
    {
        ArgumentNullException.ThrowIfNull(caller);

        if (caller.Roles.Count == 0 || caller.Roles.Overlaps(HbaRoles.BackOffice))
        {
            return;
        }

        throw new ForbiddenException("Demander ou se trouve un chauffeur nomme est reserve au back-office.");
    }

    /// <summary>
    /// La recherche de proximite. Refusee aux seuls roles qui n'ont rien a y voir.
    /// </summary>
    ///
    /// <remarks>
    /// UNE LISTE DE REFUS PLUTOT QU'UNE LISTE D'AUTORISATION, et c'est le seul endroit
    /// de ce fichier qui prend ce parti. La raison : les appelants legitimes sont de
    /// natures trop differentes pour tenir dans une liste — un jeton de service sans
    /// role, un client, un chauffeur, le back-office — et une liste d'autorisation
    /// aurait ete reecrite a chaque nouvel appelant, donc tot ou tard elargie sans
    /// qu'on y regarde.
    ///
    /// CE QUI RESTE DEHORS : le commercant et le partenaire. Ni l'un ni l'autre n'a de
    /// carte a dessiner — ils remettent un colis et suivent une livraison nommee. Un
    /// partenaire qui pourrait balayer la ville par rayons successifs reconstituerait
    /// le maillage de la flotte, qui est une information commerciale.
    /// </remarks>
    public static void EnsureCanSearchNearby(ICallerContext caller)
    {
        ArgumentNullException.ThrowIfNull(caller);

        if (caller.IsInRole(HbaRoles.Partner)
            || caller.IsInRole(HbaRoles.MerchantOwner)
            || caller.IsInRole(HbaRoles.MerchantStaff))
        {
            throw new ForbiddenException("La recherche de chauffeurs proches n'est pas ouverte a ce role.");
        }
    }
}
