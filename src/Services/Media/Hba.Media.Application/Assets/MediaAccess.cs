using Hba.BuildingBlocks.Application.Abstractions;
using Hba.BuildingBlocks.Domain;
using Hba.BuildingBlocks.Security;
using Hba.Media.Domain.Assets;

namespace Hba.Media.Application.Assets;

/// <summary>
/// Qui a le droit de quoi sur un média.
/// </summary>
///
/// <remarks>
/// QUATRE GESTIONNAIRES SUR CINQ N'AVAIENT AUCUNE VÉRIFICATION D'APPELANT, et
/// c'est le constat B3 de l'audit du 30 septembre 2026 : <c>GetMedia</c>,
/// <c>GetReadUrl</c>, <c>ListMedia</c> et <c>DeleteMedia</c> ne reposaient que
/// sur le <c>[Authorize]</c> de classe. Tout porteur d'un jeton valide qui
/// connaissait un identifiant obtenait une URL signée sur la pièce d'identité
/// d'un livreur, ou l'effaçait ; et <c>ListMedia</c>, qui prend le propriétaire
/// dans la requête, listait le dossier de n'importe qui.
///
/// CE N'EST PAS UNE RÈGLE NOUVELLE, C'EST LA SYMÉTRIQUE D'UNE RÈGLE DÉJÀ PRISE.
/// Le point 27 est tranché : « l'autorisation reste au service propriétaire […]
/// ils demandent ensuite une URL signée à Media, qui ne discute pas ». Media ne
/// peut donc pas décider si un livreur est en mission sur une course — cela
/// demanderait le métier d'un autre. Mais <c>PeutDeposer</c>, côté écriture,
/// avait déjà tiré la ligne au bon endroit : « cet appelant peut-il déposer un
/// fichier sous SON PROPRE identifiant ne demande rien à personne ». Savoir si
/// un média appartient à l'appelant ne demande rien à personne non plus. C'est
/// cette question-là, et aucune autre, qui est traitée ici.
///
/// TROIS FAMILLES D'APPELANTS :
/// <list type="bullet">
/// <item>le <c>service</c>, qui a déjà autorisé de son côté — c'est le sens de
/// « Media ne discute pas » ;</item>
/// <item>le back-office, dont c'est le métier ;</item>
/// <item>le titulaire, et lui seul, pour ce qui est à lui.</item>
/// </list>
///
/// CE QUI RESTE IMPOSSIBLE, ET DOIT LE RESTER : un jeton d'utilisateur final ne
/// lit jamais le média d'un autre. Le jour où un livreur devra voir la photo
/// d'un client, ou un client la preuve de sa livraison, c'est Delivery qui
/// l'autorisera puis demandera l'URL — avec un jeton de service, comme le
/// prévoit le point 27. Élargir cette classe serait précisément l'erreur qu'elle
/// évite.
/// </remarks>
public static class MediaAccess
{
    /// <summary>
    /// Le média est-il celui de l'appelant ?
    /// </summary>
    ///
    /// <remarks>
    /// UNE COURSE N'A PAS DE COMPTE. Une preuve de livraison appartient à la
    /// course ; personne ne peut donc « être » ce propriétaire, et
    /// <see cref="MediaOwnerType.Delivery"/> rend toujours faux ici. Seuls le
    /// système et le back-office y accèdent.
    ///
    /// L'IDENTITÉ COMPARÉE EST CELLE DU JETON, jamais celle du corps de la
    /// requête : la passerelle renseigne <c>ownerId</c> depuis le sujet du jeton
    /// (voir <c>ProfileEndpoints</c>), et c'est ce même sujet qu'on retrouve
    /// ici. Les deux bouts lisent la même source.
    /// </remarks>
    public static bool EstLeSien(ICallerContext appelant, MediaOwnerType proprietaire, string? ownerId)
    {
        ArgumentNullException.ThrowIfNull(appelant);

        if (!appelant.IsAuthenticated || string.IsNullOrWhiteSpace(ownerId))
        {
            return false;
        }

        var sien = proprietaire switch
        {
            MediaOwnerType.Customer when appelant.IsInRole(HbaRoles.Customer) => appelant.SubjectId,
            MediaOwnerType.Driver when appelant.IsInRole(HbaRoles.Driver) => appelant.SubjectId,
            MediaOwnerType.Merchant when appelant.IsInRole(HbaRoles.MerchantOwner)
                                         || appelant.IsInRole(HbaRoles.MerchantStaff) => appelant.MerchantId,
            _ => null,
        };

        // LA COMPARAISON PASSE PAR GUID, PAS PAR CHAINE. Les identifiants sont
        // des GUID des deux cotes, et leur ecriture varie — majuscules,
        // tirets — selon qui les a formates. Comparer les textes ferait
        // dependre une autorisation d'une casse.
        return sien is not null
               && Guid.TryParse(sien, out var aLui)
               && Guid.TryParse(ownerId, out var vise)
               && aLui == vise;
    }

    /// <summary>Le système, ou le back-office.</summary>
    public static bool EstUnAppelantDeConfiance(ICallerContext appelant)
    {
        ArgumentNullException.ThrowIfNull(appelant);

        return appelant.IsAuthenticated
               && (appelant.IsInRole(HbaRoles.Service) || appelant.Roles.Overlaps(HbaRoles.BackOffice));
    }

    /// <summary>
    /// Une preuve de livraison ne se lit pas comme le reste du back-office.
    /// </summary>
    ///
    /// <remarks>
    /// TRANCHÉ LE 30 SEPTEMBRE 2026, au point 7 : la preuve est conservée un
    /// mois, et l'ADMIN SEUL peut la revoir. Pas ops, pas le support, pas
    /// finance — alors que tous les quatre lisent les autres médias.
    ///
    /// CE N'EST PAS UNE PIÈCE COMME LES AUTRES. Une photo de remise cadre une
    /// porte, une cour, parfois une personne qui n'a rien demandé et qui n'est
    /// même pas cliente. La restreindre au rôle le plus étroit est le prix de
    /// la garder.
    ///
    /// LE SYSTÈME PASSE, et il le faut : c'est Delivery qui autorisera la
    /// lecture pour un donneur d'ordre, avec un jeton de service, comme le
    /// prévoit le point 27 — Media ne discute pas ce que le service
    /// propriétaire a déjà tranché.
    ///
    /// CONSÉQUENCE À CONNAÎTRE : un agent du support qui traite une réclamation
    /// ne verra pas la photo et devra passer par l'admin. C'est ce qui a été
    /// demandé ; l'élargir se fait en ajoutant un rôle ici, et nulle part
    /// ailleurs.
    /// </remarks>
    public static bool PeutVoirCeGenreDeMedia(ICallerContext appelant, MediaKind nature)
    {
        if (nature != MediaKind.DeliveryProof)
        {
            return true;
        }

        return appelant.IsInRole(HbaRoles.Service) || appelant.IsInRole(HbaRoles.Admin);
    }

    /// <summary>
    /// Lire, ou supprimer, un média désigné par son identifiant.
    /// </summary>
    ///
    /// <remarks>
    /// LE REFUS SE PRÉSENTE COMME UNE ABSENCE, et ce n'est pas de la coquetterie.
    /// Répondre « interdit » confirmerait que ce média existe, donc à qui il
    /// appartient si l'appelant a deviné juste. Répondre « introuvable » ne dit
    /// rien de plus que ce que l'appelant savait déjà. Même choix que
    /// <c>BillingAccess.EnsureCanRead</c>.
    ///
    /// ET CELA TOMBE JUSTE POUR DIRECTORY : son <c>DecrireAsync</c> attrape déjà
    /// <c>NotFound</c> et en conclut « ce média n'est pas une photo à vous », ce
    /// qui est exactement la bonne conclusion.
    /// </remarks>
    public static void EnsureCanReadAsset(
        ICallerContext appelant,
        MediaOwnerType proprietaire,
        string ownerId,
        MediaKind nature,
        Guid mediaId)
    {
        ArgumentNullException.ThrowIfNull(appelant);

        var admis = EstUnAppelantDeConfiance(appelant) || EstLeSien(appelant, proprietaire, ownerId);

        // LA NATURE RESTREINT, ELLE N'OUVRE JAMAIS. Un titulaire ou un membre du
        // back-office reste soumis à la règle propre au genre de média ; à
        // l'inverse, aucune nature ne donne accès à ce que les lignes ci-dessus
        // ont refusé.
        if (admis && PeutVoirCeGenreDeMedia(appelant, nature))
        {
            return;
        }

        throw new NotFoundException("Média", mediaId.ToString());
    }

    /// <summary>
    /// Lister les médias d'un propriétaire nommé dans la requête.
    /// </summary>
    ///
    /// <remarks>
    /// UN REFUS FRANC ICI, ET NON UNE ABSENCE : l'appelant ne devine pas un
    /// identifiant de média, il nomme un propriétaire qu'il connaît déjà. Il n'y
    /// a donc rien à lui cacher, et « interdit » est la réponse honnête.
    /// </remarks>
    public static void EnsureCanList(ICallerContext appelant, MediaOwnerType proprietaire, string ownerId)
    {
        ArgumentNullException.ThrowIfNull(appelant);

        if (EstUnAppelantDeConfiance(appelant) || EstLeSien(appelant, proprietaire, ownerId))
        {
            return;
        }

        throw new ForbiddenException("On ne liste que ses propres médias.");
    }

    /// <summary>
    /// Le seul dépôt qu'un service ait le droit de faire.
    /// </summary>
    ///
    /// <remarks>
    /// LE SYSTÈME N'AVAIT AUCUN DROIT D'ÉCRITURE, ET C'ÉTAIT JUSTE TANT QU'AUCUN
    /// SERVICE N'EN AVAIT BESOIN. Le 30 septembre 2026, la question 4 du point 7
    /// est tranchée : « Delivery porte les octets » — le livreur envoie sa photo
    /// à Delivery, qui vérifie sur SON agrégat qu'il est bien le livreur affecté,
    /// puis pousse le fichier ici avec un jeton de service. C'est la seule
    /// lecture du point 27 qui ne demande à personne de faire le métier d'un
    /// autre : Media ne sait pas qui est affecté à quelle course, et Delivery ne
    /// stocke pas de fichiers.
    ///
    /// L'OUVERTURE EST AUSSI ÉTROITE QUE LA DÉCISION. Un service ne dépose que ce
    /// qu'AUCUN UTILISATEUR ne pourrait déposer : une preuve, sous une course.
    /// Rien d'autre. Ouvrir le dépôt au rôle <c>service</c> en général donnerait
    /// à n'importe quel service le droit d'écrire une pièce d'identité sous
    /// n'importe quel livreur — et un droit qu'on accorde « au cas où » est un
    /// droit dont personne ne surveille l'usage.
    ///
    /// CE COUPLE-LÀ EST SÛR PARCE QU'IL EST INATTEIGNABLE AUTREMENT : une course
    /// n'a pas de compte, donc <see cref="EstLeSien"/> rend toujours faux pour
    /// elle, et aucun jeton d'utilisateur ne peut emprunter ce chemin.
    /// </remarks>
    public static bool EstUnDepotDeService(
        ICallerContext appelant, MediaOwnerType proprietaire, MediaKind nature)
        => appelant.IsInRole(HbaRoles.Service)
           && proprietaire == MediaOwnerType.Delivery
           && nature == MediaKind.DeliveryProof;

    /// <summary>
    /// Déposer un média pour un propriétaire.
    /// </summary>
    ///
    /// <remarks>
    /// LA NATURE EST ENTRÉE DANS LA QUESTION le 30 septembre 2026. Elle n'y
    /// était pas, parce que « peut-on déposer sous cet identifiant » se
    /// répondait sans elle ; la décision « Delivery porte les octets » a créé le
    /// premier dépôt que seule la nature autorise, et le garder hors de la
    /// signature aurait obligé l'appelant à le vérifier de son côté — c'est-à-dire
    /// à recopier une règle d'autorisation hors de l'endroit qui la porte.
    /// </remarks>
    public static bool PeutDeposer(
        ICallerContext appelant, MediaOwnerType proprietaire, string ownerId, MediaKind nature)
    {
        ArgumentNullException.ThrowIfNull(appelant);

        if (!appelant.IsAuthenticated)
        {
            return false;
        }

        return EstUnDepotDeService(appelant, proprietaire, nature)
               || appelant.Roles.Overlaps(HbaRoles.BackOffice)
               || EstLeSien(appelant, proprietaire, ownerId);
    }
}
