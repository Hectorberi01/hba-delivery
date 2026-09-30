using Grpc.Core;
using Hba.BuildingBlocks.Grpc.ServiceAuth;
using Hba.Contracts.Media.V1;
using Hba.Directory.Application.Ports;
using Microsoft.Extensions.Logging;

namespace Hba.Directory.Infrastructure.MediaAccess;

/// <summary>
/// Le port IMediaCatalogue, branché sur le service Media en gRPC.
///
/// L'ENDROIT OU LES DEUX VOCABULAIRES SE TOUCHENT. La couche applicative de
/// Directory parle de chaînes — « Customer », « ProfilePhoto » — parce qu'elle
/// n'a pas à connaître les énumérations d'un autre service. C'est ici, et
/// nulle part ailleurs, que MEDIA_OWNER_TYPE_CUSTOMER devient « Customer ».
///
/// LE DOSSIER NE S'APPELLE PAS « Media » : le contrat y déclare un message
/// « Media », et un espace de noms du même nom rend chaque mention ambiguë
/// pour le compilateur comme pour le lecteur.
///
/// LE JETON DE L'APPELANT EST REPORTE TOUT SEUL : TokenForwardingInterceptor
/// est posé par AddHbaGrpcClient. C'est ce qui fait que le journal de Media
/// enregistre « customer:&lt;id&gt; » et non « directory », donc qu'il répond à
/// la question qu'on lui posera — qui a voulu voir cette photo.
///
/// SAUF POUR UN APPEL, ET C'EST LA QU'IL MANQUAIT QUELQUE CHOSE.
/// <see cref="SupprimerParProprietaireAsync"/> descend d'un CONSOMMATEUR KAFKA —
/// « AccountErased », trente jours après la demande de suppression. Il n'y a
/// alors aucun HttpContext, donc aucun jeton à reporter : l'appel partait nu,
/// Media est en [Authorize], la réponse était « non authentifié », et le catch
/// la journalisait en avertissement sans faire échouer quoi que ce soit. La
/// fiche partait, LA PHOTO RESTAIT — alors que l'écran promet au client que
/// « passé cette date, tout disparaît sans retour possible ».
///
/// Cet appel-là demande donc un jeton de service, à la main. Les autres n'y
/// touchent pas : ils partent d'une requête d'utilisateur, et c'est SON identité
/// que le journal de Media doit garder.
/// </summary>
public sealed class MediaCatalogue(
    MediaService.MediaServiceClient client,
    IServiceTokenProvider jetons,
    ILogger<MediaCatalogue> journal) : IMediaCatalogue
{
    public async Task<MediaDecrit?> DecrireAsync(Guid mediaId, CancellationToken cancellationToken)
    {
        try
        {
            var reponse = await client.GetMediaAsync(
                new GetMediaRequest { MediaId = mediaId.ToString() },
                cancellationToken: cancellationToken);

            return new MediaDecrit(
                mediaId,
                Nom(reponse.Media.OwnerType),
                reponse.Media.OwnerId,
                Nom(reponse.Media.Kind));
        }
        catch (RpcException erreur) when (erreur.StatusCode == StatusCode.NotFound)
        {
            // UN MEDIA ABSENT N'EST PAS UNE PANNE, c'est une réponse : le
            // handler en tire « ce média n'est pas une photo à vous ». Laisser
            // remonter le RpcException aurait donné un 500 là où le client a
            // simplement présenté un identifiant qui ne vaut rien.
            return null;
        }
    }

    public async Task<LienDeLecture> LienDeLectureAsync(
        Guid mediaId,
        string motif,
        CancellationToken cancellationToken)
    {
        var reponse = await client.GetReadUrlAsync(
            new GetReadUrlRequest { MediaId = mediaId.ToString(), Reason = motif },
            cancellationToken: cancellationToken);

        return new LienDeLecture(new Uri(reponse.Url), reponse.ExpiresAt.ToDateTimeOffset());
    }

    public async Task SupprimerAsync(Guid mediaId, CancellationToken cancellationToken)
    {
        try
        {
            await client.DeleteMediaAsync(
                new DeleteMediaRequest { MediaId = mediaId.ToString() },
                cancellationToken: cancellationToken);
        }
        catch (RpcException erreur)
        {
            // ON NE FAIT PAS ECHOUER LE CLIENT POUR UN MENAGE. Cette méthode
            // est appelée APRES qu'une nouvelle photo a été enregistrée :
            // propager l'erreur rendrait un échec pour une opération qui a
            // réussi, et le client renverrait sa photo — créant un second
            // orphelin pour tenter d'en effacer un premier.
            //
            // LE NIVEAU EST « WARNING » ET NON « INFORMATION » : un fichier
            // qui survit à sa suppression est exactement ce qu'on vient
            // chercher dans les journaux le jour d'une réclamation.
            journal.LogWarning(
                erreur,
                "Le media {MediaId} n'a pas pu etre supprime ({Statut}). Il reste dans l'inventaire.",
                mediaId,
                erreur.StatusCode);
        }
    }

    public async Task SupprimerParProprietaireAsync(Guid customerId, CancellationToken cancellationToken)
    {
        try
        {
            var entetes = await jetons.AuthorizationAsync(cancellationToken).ConfigureAwait(false);

            var reponse = await client.DeleteOwnerMediaAsync(
                new DeleteOwnerMediaRequest
                {
                    OwnerType = MediaOwnerType.Customer,
                    OwnerId = customerId.ToString(),
                },
                headers: entetes,
                cancellationToken: cancellationToken);

            journal.LogInformation(
                "{Nombre} media(s) du client {CustomerId} supprime(s).",
                reponse.DeletedCount,
                customerId);
        }
        catch (RpcException erreur)
        {
            // MEME RAISON QUE SupprimerAsync : on ne fait pas echouer une
            // suppression de compte parce que le menage du stockage a rate.
            // Le « WARNING » est ce qu'on viendra chercher le jour d'une
            // reclamation — un fichier qui survit a son proprietaire est
            // exactement ce que l'inventaire permet de retrouver.
            journal.LogWarning(
                erreur,
                "Les medias du client {CustomerId} n'ont pas pu etre supprimes ({Statut}).",
                customerId,
                erreur.StatusCode);
        }
    }

    // LE NOM DE L'ENUMERATION, PAS SON NUMERO. « ProfilePhoto » se lit dans un
    // message d'erreur et dans un journal ; « 1 » oblige à ouvrir le contrat
    // pour savoir de quoi on parle.
    private static string Nom(MediaOwnerType type) => type switch
    {
        MediaOwnerType.Customer => "Customer",
        MediaOwnerType.Driver => "Driver",
        MediaOwnerType.Merchant => "Merchant",
        MediaOwnerType.Delivery => "Delivery",
        _ => "Unspecified",
    };

    private static string Nom(MediaKind kind) => kind switch
    {
        MediaKind.ProfilePhoto => "ProfilePhoto",
        MediaKind.NationalId => "NationalId",
        MediaKind.DrivingLicence => "DrivingLicence",
        MediaKind.VehicleRegistration => "VehicleRegistration",
        MediaKind.VehiclePhoto => "VehiclePhoto",
        MediaKind.DeliveryProof => "DeliveryProof",
        MediaKind.Invoice => "Invoice",
        _ => "Unspecified",
    };
}
