using Hba.BuildingBlocks.Application.Abstractions;
using Hba.BuildingBlocks.Application.Messaging;
using Hba.BuildingBlocks.Domain;
using Hba.BuildingBlocks.Security;
using Hba.BuildingBlocks.Storage;
using Hba.Media.Domain.Assets;
using Microsoft.Extensions.Logging;

namespace Hba.Media.Application.Assets;

internal static class Projection
{
    public static MediaView Vers(MediaAsset asset) => new(
        asset.Id,
        asset.OwnerType,
        asset.OwnerId,
        asset.Kind,
        asset.ContentType,
        asset.SizeBytes,
        asset.CreatedAt,
        asset.UploadedBy);
}

public sealed class StoreMediaHandler(
    IMediaRepository medias,
    IObjectStore stockage,
    IUnitOfWork unitOfWork,
    ICallerContext caller,
    IClock clock,
    ILogger<StoreMediaHandler> logger) : ICommandHandler<StoreMediaCommand, MediaView>
{
    public async Task<MediaView> HandleAsync(StoreMediaCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var maintenant = clock.UtcNow;

        var cle = CleDeStockage.Construire(
            command.OwnerType,
            command.OwnerId,
            command.Kind,
            maintenant,
            command.Extension);

        // L'OCTET AVANT LA LIGNE, ET PAS L'INVERSE.
        //
        // Écrire d'abord en base puis dans le stockage laisserait, si le
        // stockage refuse, une ligne d'inventaire qui promet un fichier
        // inexistant — et c'est l'écran du client qui le découvrirait. Dans
        // l'ordre choisi, un échec de la base laisse un objet orphelin : il
        // occupe de la place, il n'affiche rien de faux, et un balayage par
        // préfixe le retrouvera. On préfère un déchet à un mensonge.
        var depose = await stockage
            .PutAsync(cle, command.Contenu, command.SizeBytes, command.ContentType, cancellationToken)
            .ConfigureAwait(false);

        var asset = MediaAsset.Deposer(
            Guid.CreateVersion7(),
            command.OwnerType,
            command.OwnerId,
            command.Kind,
            depose.Key,
            depose.ContentType,
            depose.SizeBytes,
            caller.ToActor(),
            maintenant);

        // LE REMPLACEMENT N'A PAS LIEU ICI, ET CE FUT UN BOGUE.
        //
        // Ce handler effaçait l'ancienne photo au moment du DEPOT, parce que
        // MediaKinds.EstUnique dit qu'un propriétaire n'en a qu'une. Mais
        // déposer n'est que la PREMIERE moitié du geste : la passerelle
        // attache ensuite le nouveau média au profil, et cette seconde moitié
        // peut échouer — Directory indisponible, échéance gRPC dépassée.
        //
        // L'ANCIENNE PHOTO ETAIT ALORS DETRUITE POUR RIEN. Le profil pointait
        // toujours sur elle, son fichier n'existait plus, et l'écran retombait
        // sur les initiales sans rien dire. Le client avait perdu sa photo en
        // essayant de la changer.
        //
        // CELUI QUI EFFACE EST DESORMAIS CELUI QUI SAIT QUE LE REMPLACEMENT A
        // REUSSI : Customer.SetPhoto rend l'identifiant de la photo qu'elle
        // remplace, et SetCustomerPhotoHandler demande sa suppression APRES
        // l'enregistrement. Le pire y est un fichier orphelin — que
        // l'inventaire de Media sait retrouver — au lieu d'une perte.
        //
        // MediaKinds.EstUnique N'A PLUS AUCUN APPELANT, et il faut le dire
        // plutot que de laisser croire qu'il protege encore quelque chose. La
        // regle « un seul portrait » est desormais tenue par une COLONNE :
        // Customer.PhotoMediaId n'en porte qu'un. Ce qui reste dans
        // MediaKinds est une description exacte du domaine de Media, utile au
        // back-office le jour ou il listera les pieces d'un dossier — pas une
        // garantie en vigueur.
        medias.Add(asset);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "Media {MediaId} depose : {Kind} de {OwnerType} {OwnerId}, {Taille} octets.",
            asset.Id,
            asset.Kind,
            asset.OwnerType,
            asset.OwnerId,
            asset.SizeBytes);

        return Projection.Vers(asset);
    }
}

public sealed class GetMediaHandler(
    IMediaRepository medias,
    ICallerContext caller) : IQueryHandler<GetMediaQuery, MediaView>
{
    public async Task<MediaView> HandleAsync(GetMediaQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var asset = await medias.GetByIdAsync(query.MediaId, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException("Média", query.MediaId.ToString());

        // LA FICHE SEULE SUFFIT A NUIRE : elle nomme le propriétaire, la nature
        // du fichier et qui l'a déposé. « Ce média est la pièce d'identité du
        // livreur untel » est déjà une fuite, même sans l'octet.
        MediaAccess.EnsureCanReadAsset(caller, asset.OwnerType, asset.OwnerId, asset.Kind, query.MediaId);

        return Projection.Vers(asset);
    }
}

public sealed class GetReadUrlHandler(
    IMediaRepository medias,
    IObjectStore stockage,
    IUnitOfWork unitOfWork,
    ICallerContext caller,
    IClock clock) : IQueryHandler<GetReadUrlQuery, LienDeLecture>
{
    public async Task<LienDeLecture> HandleAsync(GetReadUrlQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var asset = await medias.GetByIdAsync(query.MediaId, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException("Média", query.MediaId.ToString());

        // AVANT DE SIGNER, PAS APRES. Une URL signée vaut cinq minutes d'accès
        // libre au fichier : la produire puis refuser l'appel l'aurait déjà
        // fabriquée, et le journal aurait consigné une lecture autorisée.
        MediaAccess.EnsureCanReadAsset(caller, asset.OwnerType, asset.OwnerId, asset.Kind, query.MediaId);

        var url = await stockage.GetReadUrlAsync(asset.StorageKey, cancellationToken).ConfigureAwait(false);

        // UNE REQUETE QUI ECRIT, ET C'EST ASSUME. Le nom dit « query », mais
        // consigner la lecture fait partie de la lecture : un journal qu'on
        // pourrait obtenir en s'abstenant d'ecrire ne prouverait rien.
        medias.Consigner(MediaAccessRecord.Consigner(asset.Id, caller.ToActor(), query.Motif, clock.UtcNow));
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new LienDeLecture(url, clock.UtcNow.Add(stockage.ReadUrlLifetime));
    }
}

public sealed class ListMediaHandler(
    IMediaRepository medias,
    ICallerContext caller)
    : IQueryHandler<ListMediaQuery, IReadOnlyList<MediaView>>
{
    public async Task<IReadOnlyList<MediaView>> HandleAsync(
        ListMediaQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        // LE PROPRIETAIRE EST UN PARAMETRE DE LA REQUETE, et c'est ce qui rendait
        // cette lecture si commode : il suffisait de nommer un livreur pour
        // obtenir l'inventaire de son dossier. On vérifie donc AVANT d'interroger
        // la base — il n'y a aucune raison de lire ce qu'on ne rendra pas.
        MediaAccess.EnsureCanList(caller, query.OwnerType, query.OwnerId);

        var assets = await medias
            .ListAsync(query.OwnerType, query.OwnerId, query.Kind, cancellationToken)
            .ConfigureAwait(false);

        // LA FICHE SEULE TRAHIT DEJA. Une preuve de livraison ne se lit que par
        // l'admin depuis le 30 septembre 2026 ; la laisser paraitre dans une
        // liste apprendrait a ops qu'elle existe, quand elle a ete prise et par
        // qui, pour une piece qu'il n'a pas le droit de voir. On retire donc ce
        // qu'on refuserait de rendre, plutot que d'annoncer son existence.
        return assets
            .Where(a => MediaAccess.PeutVoirCeGenreDeMedia(caller, a.Kind))
            .Select(Projection.Vers)
            .ToList();
    }
}

public sealed class DeleteMediaHandler(
    IMediaRepository medias,
    IObjectStore stockage,
    IUnitOfWork unitOfWork,
    ICallerContext caller) : ICommandHandler<DeleteMediaCommand, bool>
{
    public async Task<bool> HandleAsync(DeleteMediaCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var asset = await medias.GetByIdAsync(command.MediaId, cancellationToken).ConfigureAwait(false);

        // SUPPRIMER CE QUI N'EXISTE PLUS N'EST PAS UNE ERREUR. Un appelant qui
        // rejoue sa suppression apres une coupure doit obtenir le meme resultat
        // qu'au premier essai, pas un 404 qui l'inquiete.
        if (asset is null)
        {
            return false;
        }

        // L'IDEMPOTENCE CI-DESSUS NE S'ETEND PAS AU MEDIA D'AUTRUI. « Faux »
        // signifie « c'était déjà fait » : le rendre pour un fichier qu'on n'a
        // pas le droit de toucher serait un mensonge, et un mensonge rassurant.
        // Le refus se présente en absence, comme à la lecture.
        MediaAccess.EnsureCanReadAsset(caller, asset.OwnerType, asset.OwnerId, asset.Kind, command.MediaId);

        medias.Remove(asset);
        await stockage.DeleteAsync(asset.StorageKey, cancellationToken).ConfigureAwait(false);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return true;
    }
}

public sealed class DeleteOwnerMediaHandler(
    IMediaRepository medias,
    IObjectStore stockage,
    IUnitOfWork unitOfWork,
    ICallerContext caller,
    ILogger<DeleteOwnerMediaHandler> logger) : ICommandHandler<DeleteOwnerMediaCommand, int>
{
    public async Task<int> HandleAsync(DeleteOwnerMediaCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        // CETTE COMMANDE N'AVAIT AUCUNE VERIFICATION D'APPELANT.
        //
        // Elle efface TOUS les medias d'un proprietaire nomme dans la requete,
        // pieces d'identite comprises, et ne reposait que sur le [Authorize] de
        // classe : n'importe quel porteur de jeton valide pouvait effacer les
        // fichiers de n'importe qui. Aucune route de passerelle ne l'expose, ce
        // qui limite la portee au reseau interne — mais l'ADR 0007 dit que
        // l'autorisation se verifie cote service, pas que l'absence de route en
        // tient lieu.
        //
        // DEUX APPELANTS LEGITIMES, ET DEUX SEULEMENT : le service Directory,
        // quand un compte est efface pour de bon, et le back-office.
        if (!caller.IsInRole(HbaRoles.Service) && !caller.Roles.Overlaps(HbaRoles.BackOffice))
        {
            throw new ForbiddenException(
                "Effacer les medias d'un proprietaire releve du systeme ou du back-office.");
        }

        var assets = await medias
            .ListAsync(command.OwnerType, command.OwnerId, kind: null, cancellationToken)
            .ConfigureAwait(false);

        foreach (var asset in assets)
        {
            medias.Remove(asset);
            await stockage.DeleteAsync(asset.StorageKey, cancellationToken).ConfigureAwait(false);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // EN AVERTISSEMENT, PARCE QUE C'EST IRREVERSIBLE. Une suppression de
        // compte qui emporte des pieces d'identite merite une ligne qu'on
        // retrouve, pas une information noyee dans le trafic courant.
        logger.LogWarning(
            "Suppression de {Nombre} media(s) de {OwnerType} {OwnerId}.",
            assets.Count,
            command.OwnerType,
            command.OwnerId);

        return assets.Count;
    }
}
