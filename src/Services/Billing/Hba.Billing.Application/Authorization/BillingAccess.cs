using Hba.BuildingBlocks.Application.Abstractions;
using Hba.BuildingBlocks.Domain;
using Hba.BuildingBlocks.Security;

namespace Hba.Billing.Application.Authorization;

/// <summary>Les deux seuls titulaires de compte. Voir <c>BillingAccount.OwnerType</c>.</summary>
public static class BillingOwnerTypes
{
    public const string Merchant = "merchant";
    public const string Partner = "partner";
}

/// <summary>
/// Autorisation du service Billing, vérifiée côté service.
/// </summary>
///
/// <remarks>
/// CE SERVICE N'EST PAS PROTÉGÉ PAR LE FAIT DE N'ÊTRE APPELÉ QUE PAR DELIVERY.
/// C'est la confusion que ce fichier corrige.
///
/// Delivery appelle Billing à travers <c>TokenForwardingInterceptor</c>, qui
/// REPORTE LE JETON DE L'UTILISATEUR FINAL — sa documentation le dit mot pour
/// mot : « c'est ce qui permet au service appelé de refaire lui-même la
/// vérification d'autorisation, au lieu de faire confiance au BFF ». Le jeton
/// qui arrive ici est donc celui d'un commerçant ou d'un partenaire, pas celui
/// d'un service de confiance. Avec le seul <c>[Authorize]</c>, N'IMPORTE QUEL
/// PORTEUR DE JETON VALIDE pouvait débiter n'importe quel compte, et surtout
/// CRÉDITER LE SIEN — c'est-à-dire se donner du solde sans payer.
///
/// TROIS NIVEAUX, ET LA DIFFÉRENCE EST CELLE DE L'ARGENT :
/// débiter appauvrit le titulaire, donc le titulaire peut le faire pour
/// lui-même ; créditer l'enrichit, donc personne ne le fait pour soi ; lire ne
/// change rien, donc le titulaire et le back-office le font librement.
/// </remarks>
public static class BillingAccess
{
    /// <summary>
    /// Droit de débiter un compte. Seul son titulaire, et pour lui-même.
    /// </summary>
    ///
    /// <remarks>
    /// LE BACK-OFFICE N'EST PAS ADMIS ICI, ET C'EST UN CHOIX DE MOINDRE
    /// PRIVILÈGE. Un débit hors course n'a aujourd'hui aucun cas d'usage :
    /// corriger un compte trop crédité se ferait par <c>BillingAccount.Adjust</c>,
    /// qui admet les montants négatifs mais n'est PAS exposé en gRPC. Tant qu'il
    /// ne l'est pas, réduire un solde à la main est impossible — c'est un manque
    /// assumé, noté dans points-a-trancher, et non une raison d'ouvrir Debit à
    /// tout le back-office.
    ///
    /// UN EMPLOYÉ DE COMMERÇANT N'EST PAS ADMIS NON PLUS : il ne crée pas de
    /// livraison (<c>ResolveOrderer</c> le refuse), donc il n'a rien à débiter.
    /// </remarks>
    public static void EnsureCanDebit(ICallerContext caller, string ownerType, string ownerId)
    {
        ArgumentNullException.ThrowIfNull(caller);

        EnsureAuthenticated(caller);

        if (IsOwner(caller, ownerType, ownerId))
        {
            return;
        }

        // FORBIDDEN ET NON NOTFOUND, CONTRAIREMENT À LA LECTURE. Refuser un
        // débit ne révèle rien : l'appelant nomme le compte, il sait déjà qu'il
        // n'est pas le sien. Et un « compte introuvable » enverrait celui qui
        // débogue chercher en base un compte qui existe très bien.
        throw new ForbiddenException("Seul le titulaire d'un compte peut le débiter.");
    }

    /// <summary>
    /// Droit de créditer un compte. Back-office financier uniquement.
    /// </summary>
    ///
    /// <remarks>
    /// LE TITULAIRE NE SE CRÉDITE JAMAIS LUI-MÊME. C'est la faille que cette
    /// méthode ferme : un commerçant qui pouvait appeler <c>Credit</c> sur son
    /// propre compte se donnait du solde, donc des courses gratuites.
    ///
    /// OPS ET SUPPORT NON PLUS. Le back-office n'est pas un bloc : constater un
    /// paiement reçu est le métier de finance, et la décision du 29 septembre
    /// 2026 était « recharge à la main par finance, pour commencer ». Admin
    /// reste admis parce qu'il porte déjà tous les droits par construction.
    ///
    /// LE JOUR OÙ LA RECHARGE SERA AUTOMATIQUE, ELLE N'ENTRERA PAS PAR ICI : le
    /// webhook de l'opérateur est vérifié par Payment et consommé par l'Inbox de
    /// Billing, en arrière-plan, SANS JETON D'UTILISATEUR. Ce chemin n'existe
    /// pas encore ; quand il existera, il lui faudra un contexte d'appel système
    /// — et non un assouplissement de cette règle.
    /// </remarks>
    public static void EnsureCanCredit(ICallerContext caller)
    {
        ArgumentNullException.ThrowIfNull(caller);

        EnsureAuthenticated(caller);

        if (caller.IsInRole(HbaRoles.Finance) || caller.IsInRole(HbaRoles.Admin))
        {
            return;
        }

        throw new ForbiddenException("Seul le back-office financier peut créditer un compte.");
    }

    /// <summary>
    /// Droit d'ouvrir un compte. Back-office financier uniquement.
    /// </summary>
    ///
    /// <remarks>
    /// OUVRIR SON PROPRE COMPTE EST INOFFENSIF — il naît à zéro — MAIS EN OUVRIR
    /// UN POUR AUTRUI NE L'EST PAS : le titulaire est nommé dans la requête, et
    /// un compte créé au nom d'un partenaire avec un seuil d'alerte choisi par
    /// un tiers est un compte que personne n'a décidé. On ferme donc la porte
    /// entièrement, d'autant que l'ouverture accompagne de toute façon un
    /// contrat commercial, c'est-à-dire finance.
    /// </remarks>
    public static void EnsureCanOpen(ICallerContext caller)
    {
        ArgumentNullException.ThrowIfNull(caller);

        EnsureAuthenticated(caller);

        if (caller.IsInRole(HbaRoles.Finance) || caller.IsInRole(HbaRoles.Admin))
        {
            return;
        }

        throw new ForbiddenException("Seul le back-office financier peut ouvrir un compte de facturation.");
    }

    /// <summary>
    /// Droit d'annuler un débit : LE SYSTÈME, ou le back-office financier.
    /// JAMAIS LE TITULAIRE.
    /// </summary>
    ///
    /// <remarks>
    /// CETTE PORTE A ÉTÉ OUVERTE AU TITULAIRE LE 29 SEPTEMBRE 2026, SUR UN
    /// RAISONNEMENT FAUX, ET REFERMÉE LE 30.
    ///
    /// L'argument écrit ici disait : « annuler ne choisit aucun montant, il est
    /// lu sur le débit qu'on annule ; le pire qu'un titulaire puisse obtenir est
    /// de récupérer son propre argent ». La deuxième moitié est fausse, et c'est
    /// la seule qui comptait. **LA CLÉ DU DÉBIT EST L'IDENTIFIANT DE LA
    /// COURSE**, et Delivery le rend au donneur d'ordre dans la réponse de
    /// création. Un partenaire appelait donc `ReverseDebit` avec l'identifiant de
    /// sa course EN COURS : il était remboursé et gardait sa livraison. Le pire
    /// qu'il pouvait obtenir n'était pas son argent, c'était une course gratuite.
    ///
    /// BILLING NE PEUT PAS DISTINGUER LES DEUX CAS, ET N'A PAS À LE POUVOIR. Il
    /// ne connaît pas les courses. Un débit sans contrepartie et un débit dont la
    /// contrepartie existe lui sont identiques : seul l'APPELANT sait lequel est
    /// lequel. La règle est donc sur l'appelant, pas sur le montant.
    ///
    /// LE SEUL APPELANT LÉGITIME EST LE SYSTÈME. Delivery compense un échec de
    /// création — il est le seul à savoir que la course n'existe pas —, et il
    /// force son JETON DE SERVICE sur cet appel précis, au lieu de reporter celui
    /// du donneur d'ordre. `finance` reste admis pour les corrections à la main.
    /// Le bénéficiaire, lui, n'a plus rien à faire ici.
    ///
    /// CE QUI RESTE VRAI DE L'ANCIEN ARGUMENT : le montant n'est pas choisi, et
    /// la clé de l'annulation est unique en base, donc un rejeu ne rend pas
    /// l'argent deux fois. Ce n'était simplement pas la question.
    /// </remarks>
    public static void EnsureCanReverse(ICallerContext caller, string ownerType, string ownerId)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerType);
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);

        EnsureAuthenticated(caller);

        if (caller.IsInRole(HbaRoles.Service)
            || caller.IsInRole(HbaRoles.Finance)
            || caller.IsInRole(HbaRoles.Admin))
        {
            return;
        }

        // LE TITULAIRE EST REFUSÉ NOMMÉMENT, et le message le dit sans détour :
        // c'est la porte qu'il a fallu refermer, et un lecteur pressé la
        // rouvrirait en croyant corriger un oubli.
        throw new ForbiddenException(
            "Un titulaire n'annule pas son propre débit : la course existerait encore. "
            + "Cet appel revient au système, ou au back-office financier.");
    }

    /// <summary>
    /// Droit de lire un compte : son titulaire, ou le back-office.
    /// </summary>
    ///
    /// <remarks>
    /// NOTFOUND HORS PÉRIMÈTRE, comme pour les livraisons : rien ne doit
    /// permettre de savoir si un concurrent a un compte chez HBA, ni quels
    /// identifiants de commerçants existent. Un « interdit » sur un compte
    /// existant et un « introuvable » sur un compte absent se distingueraient
    /// l'un de l'autre, ce qui suffit à énumérer.
    ///
    /// TOUT LE BACK-OFFICE LIT, support compris : répondre à « pourquoi ma
    /// course est refusée » demande de voir un solde, et voir un solde ne le
    /// change pas.
    /// </remarks>
    public static void EnsureCanRead(ICallerContext caller, string ownerType, string ownerId)
    {
        ArgumentNullException.ThrowIfNull(caller);

        EnsureAuthenticated(caller);

        if (caller.Roles.Overlaps(HbaRoles.BackOffice) || IsOwner(caller, ownerType, ownerId))
        {
            return;
        }

        throw new NotFoundException("Compte de facturation", $"{ownerType}:{ownerId}");
    }

    /// <summary>
    /// L'appelant EST-IL le titulaire nommé. Comparaison ordinale : un
    /// identifiant n'est pas un texte à comparer « à peu près ».
    /// </summary>
    private static bool IsOwner(ICallerContext caller, string ownerType, string ownerId)
    {
        if (string.IsNullOrWhiteSpace(ownerType) || string.IsNullOrWhiteSpace(ownerId))
        {
            return false;
        }

        if (string.Equals(ownerType, BillingOwnerTypes.Merchant, StringComparison.Ordinal))
        {
            return caller.IsInRole(HbaRoles.MerchantOwner)
                   && !string.IsNullOrWhiteSpace(caller.MerchantId)
                   && string.Equals(caller.MerchantId, ownerId, StringComparison.Ordinal);
        }

        if (string.Equals(ownerType, BillingOwnerTypes.Partner, StringComparison.Ordinal))
        {
            // LE PARTENAIRE N'EST PAS LE COMMERÇANT, et le référentiel interdit
            // de les confondre : un jeton commerçant ne devient jamais titulaire
            // d'un compte « partner », même si le commerçant appartient à ce
            // partenaire.
            return caller.IsInRole(HbaRoles.Partner)
                   && !string.IsNullOrWhiteSpace(caller.PartnerId)
                   && string.Equals(caller.PartnerId, ownerId, StringComparison.Ordinal);
        }

        return false;
    }

    private static void EnsureAuthenticated(ICallerContext caller)
    {
        if (!caller.IsAuthenticated)
        {
            throw new ForbiddenException("Appel non authentifié.");
        }
    }
}
