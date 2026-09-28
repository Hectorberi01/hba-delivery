using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Hba.BuildingBlocks.Application.Messaging;
using Hba.Contracts.Media.V1;
using Hba.Media.Application.Assets;
using Microsoft.AspNetCore.Authorization;
using DomainKind = Hba.Media.Domain.Assets.MediaKind;
using DomainOwner = Hba.Media.Domain.Assets.MediaOwnerType;
using ProtoMedia = Hba.Contracts.Media.V1.Media;

namespace Hba.Media.Api.Grpc;

/// <summary>
/// Entrée synchrone de Media : des métadonnées, jamais des octets.
///
/// AUCUNE METHODE N'EST ANONYME. Un média se demande toujours au nom de
/// quelqu'un — et c'est ce quelqu'un que le journal des consultations
/// enregistre.
/// </summary>
[Authorize]
public sealed class MediaGrpcService(IDispatcher dispatcher) : MediaService.MediaServiceBase
{
    public override async Task<GetMediaResponse> GetMedia(GetMediaRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        var view = await dispatcher.QueryAsync(
            new GetMediaQuery(Identifiant(request.MediaId)),
            context.CancellationToken).ConfigureAwait(false);

        return new GetMediaResponse { Media = ToProto(view) };
    }

    public override async Task<GetReadUrlResponse> GetReadUrl(
        GetReadUrlRequest request,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        var lien = await dispatcher.QueryAsync(
            new GetReadUrlQuery(Identifiant(request.MediaId), request.Reason),
            context.CancellationToken).ConfigureAwait(false);

        return new GetReadUrlResponse
        {
            Url = lien.Url.ToString(),
            ExpiresAt = Timestamp.FromDateTimeOffset(lien.ExpiresAt),
        };
    }

    public override async Task<ListMediaResponse> ListMedia(ListMediaRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        var vues = await dispatcher.QueryAsync(
            new ListMediaQuery(
                (DomainOwner)request.OwnerType,
                request.OwnerId,
                request.Kind == MediaKind.Unspecified ? null : (DomainKind)request.Kind),
            context.CancellationToken).ConfigureAwait(false);

        var reponse = new ListMediaResponse();
        reponse.Media.AddRange(vues.Select(ToProto));

        return reponse;
    }

    public override async Task<DeleteMediaResponse> DeleteMedia(
        DeleteMediaRequest request,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        var supprime = await dispatcher.SendAsync(
            new DeleteMediaCommand(Identifiant(request.MediaId)),
            context.CancellationToken).ConfigureAwait(false);

        return new DeleteMediaResponse { Deleted = supprime };
    }

    public override async Task<DeleteOwnerMediaResponse> DeleteOwnerMedia(
        DeleteOwnerMediaRequest request,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        var nombre = await dispatcher.SendAsync(
            new DeleteOwnerMediaCommand((DomainOwner)request.OwnerType, request.OwnerId),
            context.CancellationToken).ConfigureAwait(false);

        return new DeleteOwnerMediaResponse { DeletedCount = nombre };
    }

    // LES ENUMS DU CONTRAT ET DU DOMAINE PORTENT LES MEMES NUMEROS, et la
    // conversion directe en depend. Ce n'est pas un hasard : les deux sont
    // ecrites cote a cote et un test le verifiera. Le jour ou l'une bouge sans
    // l'autre, des cartes d'identite deviendraient des photos de profil.
    private static ProtoMedia ToProto(MediaView view) => new()
    {
        Id = view.Id.ToString(),
        OwnerType = (MediaOwnerType)view.OwnerType,
        OwnerId = view.OwnerId,
        Kind = (MediaKind)view.Kind,
        ContentType = view.ContentType,
        SizeBytes = view.SizeBytes,
        CreatedAt = Timestamp.FromDateTimeOffset(view.CreatedAt),
        UploadedBy = view.UploadedBy,
    };

    private static Guid Identifiant(string valeur)
        => Guid.TryParse(valeur, out var id)
            ? id
            : throw new RpcException(new Status(StatusCode.InvalidArgument, $"Identifiant illisible : {valeur}."));
}
