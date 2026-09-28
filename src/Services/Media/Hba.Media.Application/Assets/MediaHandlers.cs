using Hba.BuildingBlocks.Application.Abstractions;
using Hba.BuildingBlocks.Application.Messaging;
using Hba.BuildingBlocks.Domain;
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

        // UNE NATURE UNIQUE REMPLACE, LES AUTRES S'AJOUTENT. Personne n'a deux
        // visages ; une pièce corrigée, en revanche, ne doit pas effacer celle
        // qu'ops est en train de comparer.
        if (MediaKinds.EstUnique(command.Kind))
        {
            var anciens = await medias
                .ListAsync(command.OwnerType, command.OwnerId, command.Kind, cancellationToken)
                .ConfigureAwait(false);

            foreach (var ancien in anciens)
            {
                medias.Remove(ancien);
                await stockage.DeleteAsync(ancien.StorageKey, cancellationToken).ConfigureAwait(false);
            }
        }

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

public sealed class GetMediaHandler(IMediaRepository medias) : IQueryHandler<GetMediaQuery, MediaView>
{
    public async Task<MediaView> HandleAsync(GetMediaQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var asset = await medias.GetByIdAsync(query.MediaId, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException("Média", query.MediaId.ToString());

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

        var url = await stockage.GetReadUrlAsync(asset.StorageKey, cancellationToken).ConfigureAwait(false);

        // UNE REQUETE QUI ECRIT, ET C'EST ASSUME. Le nom dit « query », mais
        // consigner la lecture fait partie de la lecture : un journal qu'on
        // pourrait obtenir en s'abstenant d'ecrire ne prouverait rien.
        medias.Consigner(MediaAccessRecord.Consigner(asset.Id, caller.ToActor(), query.Motif, clock.UtcNow));
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new LienDeLecture(url, clock.UtcNow.Add(stockage.ReadUrlLifetime));
    }
}

public sealed class ListMediaHandler(IMediaRepository medias)
    : IQueryHandler<ListMediaQuery, IReadOnlyList<MediaView>>
{
    public async Task<IReadOnlyList<MediaView>> HandleAsync(
        ListMediaQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var assets = await medias
            .ListAsync(query.OwnerType, query.OwnerId, query.Kind, cancellationToken)
            .ConfigureAwait(false);

        return assets.Select(Projection.Vers).ToList();
    }
}

public sealed class DeleteMediaHandler(
    IMediaRepository medias,
    IObjectStore stockage,
    IUnitOfWork unitOfWork) : ICommandHandler<DeleteMediaCommand, bool>
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
    ILogger<DeleteOwnerMediaHandler> logger) : ICommandHandler<DeleteOwnerMediaCommand, int>
{
    public async Task<int> HandleAsync(DeleteOwnerMediaCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

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
