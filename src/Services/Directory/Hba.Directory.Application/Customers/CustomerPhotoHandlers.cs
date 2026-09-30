using Hba.BuildingBlocks.Application.Abstractions;
using Hba.BuildingBlocks.Application.Messaging;
using Hba.BuildingBlocks.Domain;
using Hba.Directory.Application.Authorization;
using Hba.Directory.Application.Ports;
using Hba.Directory.Application.Views;
using Microsoft.Extensions.Logging;

namespace Hba.Directory.Application.Customers;

/// <summary>
/// La règle commune aux trois gestes de la photo de profil.
///
/// POURQUOI C'EST DIRECTORY QUI LA PORTE, ET PAS MEDIA. Le point 27 l'a tranché
/// le 28 septembre 2026 : Media tient l'inventaire, écrit, signe et supprime,
/// mais ne décide pas qui voit quoi. Savoir si un livreur peut voir le portrait
/// d'un client suppose de savoir s'il est en mission sur SA course — une
/// question que Media ne peut pas poser sans connaître le métier des autres.
/// L'autorisation reste donc au service qui détient la donnée.
/// </summary>
internal static class PhotoDuClient
{
    /// <summary>Ce qu'un média doit être pour servir de portrait.</summary>
    public const string ProprietaireAttendu = "Customer";

    /// <summary>Idem pour sa nature.</summary>
    public const string NatureAttendue = "ProfilePhoto";

    /// <summary>
    /// Vérifie qu'un média peut devenir le portrait de ce client.
    /// </summary>
    ///
    /// <remarks>
    /// SANS CE CONTROLE, UN IDENTIFIANT DEVINE SUFFIT. Les identifiants de
    /// média sont des GUID v7 : ils se devinent mal, mais « mal » n'est pas
    /// « pas ». Un client qui en présenterait un autre ferait afficher comme
    /// son portrait la pièce d'identité d'un livreur — et le journal de Media
    /// enregistrerait sagement que c'est Directory qui l'a demandée.
    ///
    /// LE MESSAGE NE DIT PAS CE QU'EST LE MEDIA VISE. « Ce média n'est pas une
    /// photo de profil vous appartenant » suffit : détailler confirmerait
    /// l'existence et la nature d'un fichier qui appartient à quelqu'un
    /// d'autre.
    /// </remarks>
    public static async Task VerifierAsync(
        IMediaCatalogue media,
        Guid mediaId,
        Guid customerId,
        CancellationToken cancellationToken)
    {
        var decrit = await media.DecrireAsync(mediaId, cancellationToken).ConfigureAwait(false);

        var convient = decrit is not null
            && string.Equals(decrit.OwnerType, ProprietaireAttendu, StringComparison.OrdinalIgnoreCase)
            && string.Equals(decrit.Kind, NatureAttendue, StringComparison.OrdinalIgnoreCase)
            && Guid.TryParse(decrit.OwnerId, out var proprietaire)
            && proprietaire == customerId;

        if (!convient)
        {
            throw new DomainException(
                "MEDIA_NOT_A_PROFILE_PHOTO",
                "Ce média n'est pas une photo de profil vous appartenant.");
        }
    }
}

public sealed class SetCustomerPhotoHandler(
    ICustomerRepository customers,
    IUnitOfWork unitOfWork,
    IMediaCatalogue media,
    ICallerContext caller,
    IClock clock) : ICommandHandler<SetCustomerPhotoCommand, CustomerView>
{
    public async Task<CustomerView> HandleAsync(
        SetCustomerPhotoCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var id = DirectoryAccess.ResolveCustomerId(caller, null);

        var customer = await customers.GetByIdAsync(id, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException("Client", id.ToString());

        await PhotoDuClient.VerifierAsync(media, command.MediaId, id, cancellationToken)
            .ConfigureAwait(false);

        var remplacee = customer.SetPhoto(command.MediaId, caller.ToActor(), clock.UtcNow);

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // APRES L'ENREGISTREMENT, JAMAIS AVANT. Effacer d'abord puis échouer
        // laisserait le client avec une colonne qui pointe sur un fichier
        // détruit : un portrait cassé, sans moyen de revenir en arrière. Dans
        // cet ordre-ci, le pire est un fichier orphelin — que l'inventaire de
        // Media sait retrouver.
        if (remplacee is not null)
        {
            await media.SupprimerAsync(remplacee.Value, cancellationToken).ConfigureAwait(false);
        }

        return DirectoryViewMapper.ToView(customer);
    }
}

public sealed class RemoveCustomerPhotoHandler(
    ICustomerRepository customers,
    IUnitOfWork unitOfWork,
    IMediaCatalogue media,
    ICallerContext caller,
    IClock clock) : ICommandHandler<RemoveCustomerPhotoCommand, CustomerView>
{
    public async Task<CustomerView> HandleAsync(
        RemoveCustomerPhotoCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var id = DirectoryAccess.ResolveCustomerId(caller, null);

        var customer = await customers.GetByIdAsync(id, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException("Client", id.ToString());

        var effacee = customer.RemovePhoto(caller.ToActor(), clock.UtcNow);

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // LE FICHIER PART VRAIMENT. « Retirer ma photo » qui se contenterait
        // d'oublier la colonne laisserait le portrait dans le stockage : le
        // client croirait l'avoir effacé, et il y serait toujours.
        if (effacee is not null)
        {
            await media.SupprimerAsync(effacee.Value, cancellationToken).ConfigureAwait(false);
        }

        return DirectoryViewMapper.ToView(customer);
    }
}

/// <summary>
/// Le lien d'affichage.
///
/// C'EST ICI QUE SE VERIFIE LE DROIT DE VOIR. ResolveCustomerId fait tout le
/// travail : le client lui-même passe, le back-office passe sur n'importe quelle
/// fiche, et un tiers reçoit « Client introuvable » — pas « interdit », qui
/// confirmerait que la fiche existe.
///
/// CE QUI N'EST PAS ENCORE LA : LE LIVREUR EN MISSION. La règle a été arrêtée —
/// le client, et le livreur affecté à sa course en cours — mais Directory ne
/// sait pas ce qu'est une course. Voir le point 27 des points à trancher.
/// </summary>
public sealed class GetCustomerPhotoLinkHandler(
    ICustomerRepository customers,
    IMediaCatalogue media,
    ICallerContext caller,
    ILogger<GetCustomerPhotoLinkHandler> journal)
    : IQueryHandler<GetCustomerPhotoLinkQuery, PhotoLinkView>
{
    public async Task<PhotoLinkView> HandleAsync(
        GetCustomerPhotoLinkQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var id = DirectoryAccess.ResolveCustomerId(caller, query.CustomerId);

        var customer = await customers.GetByIdAsync(id, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException("Client", id.ToString());

        var mediaId = customer.PhotoMediaId
            ?? throw new NotFoundException("Photo de profil", id.ToString());

        // LE MOTIF DISTINGUE LES DEUX LECTEURS, et c'est tout l'intérêt du
        // journal. « Le client consulte son propre profil » est du bruit ;
        // « le back-office ouvre la fiche d'un client » est ce qu'on viendra y
        // chercher le jour où quelqu'un demandera qui a vu sa photo.
        var soiMeme = string.Equals(caller.SubjectId, id.ToString(), StringComparison.OrdinalIgnoreCase);

        var motif = soiMeme
            ? "Le client consulte son propre profil."
            : "Le back-office consulte la fiche d'un client.";

        if (!soiMeme)
        {
            journal.LogInformation(
                "Photo du client {CustomerId} demandee par {Lecteur}.",
                id,
                caller.SubjectId);
        }

        var lien = await media.LienDeLectureAsync(mediaId, motif, cancellationToken).ConfigureAwait(false);

        return new PhotoLinkView(lien.Url, lien.ExpiresAt);
    }
}
