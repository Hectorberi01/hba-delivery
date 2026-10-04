using Hba.BuildingBlocks.Application.Abstractions;
using Hba.BuildingBlocks.Domain;
using Hba.BuildingBlocks.Security;

namespace Hba.Pricing.Application.Authorization;

/// <summary>
/// Autorisation du service Pricing, vérifiée côté service.
/// </summary>
///
/// <remarks>
/// DEMANDER UN PRIX N'EST PAS ADMINISTRER LES PRIX, et c'est toute la
/// distinction que porte ce fichier. N'importe quel appelant authentifié peut
/// demander un devis : c'est le geste normal d'un client qui regarde combien
/// coûte une course. Écrire la grille qui produit ce devis est autre chose.
///
/// UNE GRILLE EST DE L'ARGENT, DANS LES DEUX SENS. Elle décide de ce que paie
/// le client ET de ce que touche le livreur — <c>DriverShareBasisPoints</c> est
/// dans le même objet. Quelqu'un qui pourrait l'écrire pourrait doubler les
/// prix d'une ville, ou ramener la part du livreur à un pour cent, sans qu'un
/// seul paiement soit modifié : la fraude ne laisserait aucune trace dans
/// Payment, seulement dans des devis qui paraîtraient normaux.
///
/// LE REFUS PORTE LE CODE « FORBIDDEN », PAS UN CODE PROPRE À PRICING, et ce
/// n'est pas un renoncement. <c>ForbiddenException</c> impose ce code unique à
/// tout le dépôt : le client en tire un 403, et ce qui distingue un refus d'un
/// autre est le MESSAGE, pas un code que personne ne branche. Un code sur mesure
/// aurait promis une finesse de traitement qu'aucune application n'exploite.
///
/// LE SERVICE NE FAIT PAS CONFIANCE À LA PASSERELLE (ADR 0007). Celle-ci
/// applique déjà <c>HbaPolicies.Finance</c>, mais le jeton qui arrive ici est
/// reporté par <c>TokenForwardingInterceptor</c> : c'est celui de l'utilisateur
/// final, pas celui d'un service de confiance. Sans la vérification ci-dessous,
/// tout porteur d'un jeton valide atteignant le service directement pourrait
/// écrire une grille.
/// </remarks>
public static class PricingAccess
{
    /// <summary>
    /// Droit de créer ou de clore une grille tarifaire.
    /// </summary>
    ///
    /// <remarks>
    /// ADMIN ET FINANCE, ET PERSONNE D'AUTRE. Décidé le 1er octobre 2026.
    /// C'est la même population que <c>HbaPolicies.Finance</c>, qui vaut déjà
    /// « admin ou finance » — les deux rôles qui répondent de l'argent.
    ///
    /// OPS ET SUPPORT EN SONT EXCLUS, bien qu'ils soient du back-office. Ils
    /// traitent des courses, pas des barèmes : un incident de livraison ne se
    /// résout jamais en changeant le prix de toute une zone. C'est du moindre
    /// privilège, pas de la défiance.
    ///
    /// UN JETON DE SERVICE N'EST PAS ADMIS NON PLUS. Aucun service n'a de raison
    /// d'écrire une grille : le prix vient d'une décision humaine. Laisser
    /// passer <c>HbaRoles.Service</c> ouvrirait l'écriture des barèmes à
    /// n'importe quel service compromis.
    /// </remarks>
    public static void EnsureCanAdministerTariffs(ICallerContext caller)
    {
        ArgumentNullException.ThrowIfNull(caller);

        if (!caller.IsAuthenticated)
        {
            throw new ForbiddenException("L'administration des grilles demande une identité.");
        }

        if (caller.IsInRole(HbaRoles.Admin) || caller.IsInRole(HbaRoles.Finance))
        {
            return;
        }

        throw new ForbiddenException(
            "Seuls l'administration et la finance écrivent les grilles tarifaires.");
    }

    /// <summary>
    /// Droit de dessiner, renommer, repriorizer ou désactiver une zone.
    /// </summary>
    ///
    /// <remarks>
    /// ADMIN SEUL, ET C'EST PLUS ÉTROIT QUE POUR UNE GRILLE. Décidé le 1er octobre
    /// 2026 (point 40). Le référentiel confie la configuration complète à `admin`,
    /// et dessiner une zone en est : la finance fixe les prix dans une géographie
    /// que l'administration définit.
    ///
    /// LA RAISON EST LA MÊME QUE POUR LES GRILLES, PAR UN AUTRE CHEMIN. Redessiner
    /// une zone **déplace des prix sans toucher à une seule grille** : un point qui
    /// passe de « périphérie » à « centre » change de barème au prochain devis. La
    /// fraude ne laisserait aucune trace dans Payment ni dans la table des grilles,
    /// seulement dans un polygone que personne ne relit.
    ///
    /// UN JETON DE SERVICE N'EST PAS ADMIS. Une zone vient d'une décision humaine ;
    /// aucun service n'a de raison d'en dessiner une.
    /// </remarks>
    public static void EnsureCanAdministerZones(ICallerContext caller)
    {
        ArgumentNullException.ThrowIfNull(caller);

        if (!caller.IsAuthenticated)
        {
            throw new ForbiddenException("L'administration des zones demande une identité.");
        }

        if (caller.IsInRole(HbaRoles.Admin))
        {
            return;
        }

        throw new ForbiddenException("Seule l'administration dessine les zones tarifaires.");
    }

    /// <summary>
    /// Droit de lire les zones et leurs contours.
    /// </summary>
    ///
    /// <remarks>
    /// TOUT LE BACK-OFFICE, ET PLUS LARGE QUE L'ÉCRITURE EXPRÈS. La carte des zones
    /// est de la supervision : ops en a besoin pour comprendre pourquoi une course a
    /// été chiffrée à ce prix-là et dans quel périmètre un livreur a été cherché.
    /// Lire une forme ne déplace aucun prix.
    ///
    /// PAS OUVERT AUX CLIENTS NI AUX LIVREURS, en revanche. Un découpage tarifaire
    /// est une information commerciale : publier les limites exactes des zones
    /// inviterait à faire deux pas de côté pour changer de barème.
    /// </remarks>
    public static void EnsureCanReadZones(ICallerContext caller)
    {
        ArgumentNullException.ThrowIfNull(caller);

        if (!caller.IsAuthenticated)
        {
            throw new ForbiddenException("La lecture des zones demande une identité.");
        }

        if (caller.Roles.Overlaps(HbaRoles.BackOffice))
        {
            return;
        }

        throw new ForbiddenException("Les zones tarifaires ne se lisent qu'en back-office.");
    }
}
