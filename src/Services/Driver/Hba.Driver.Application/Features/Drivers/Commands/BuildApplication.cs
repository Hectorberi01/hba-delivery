using Hba.BuildingBlocks.Application.Abstractions;
using Hba.BuildingBlocks.Application.Messaging;
using Hba.BuildingBlocks.Domain;
using Hba.Driver.Application.Common.Interfaces;
using Hba.Driver.Application.Common.Views;
using Hba.Driver.Domain.Drivers;
using Hba.Driver.Domain.Exceptions;

namespace Hba.Driver.Application.Features.Drivers.Commands;

/// <summary>
/// Constitution du dossier par le livreur (ADR 0021).
///
/// LE FLUX S'ARRETE ICI. La passerelle relaie les octets jusqu'au service ;
/// c'est ce handler qui ecrit dans le stockage, enregistre la piece, et
/// supprime l'objet si l'enregistrement echoue. Faire ecrire la passerelle
/// lui aurait donne les identifiants du stockage et laisse une piece
/// d'identite orpheline apres chaque refus.
///
/// UN FLUX DANS UNE COMMANDE N'EST PAS UNE HABITUDE A PRENDRE. Il est ici
/// parce que l'ecriture et l'enregistrement doivent reussir ou echouer
/// ensemble ; les separer en deux commandes recreerait le trou qu'on ferme.
/// </summary>
public sealed record UploadDocumentCommand(
    Guid DriverId,
    DocumentType Type,
    Stream Content,
    long SizeBytes,
    string ContentType,
    string Extension) : ICommand<DriverView>;

public sealed record DeclareVehicleCommand(
    Guid DriverId,
    VehicleType Type,
    string Plate,
    int CapacityGrams) : ICommand<DriverView>;

public sealed record UploadProfilePhotoCommand(
    Guid DriverId,
    Stream Content,
    long SizeBytes,
    string ContentType,
    string Extension) : ICommand<DriverView>;

public sealed record SubmitApplicationCommand(Guid DriverId) : ICommand<DriverView>;

public sealed class UploadDocumentHandler(
    IDriverRepository drivers,
    IObjectStore objets,
    IUnitOfWork unitOfWork,
    ICallerContext caller,
    IClock clock) : ICommandHandler<UploadDocumentCommand, DriverView>
{
    public async Task<DriverView> HandleAsync(UploadDocumentCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        // LE DOSSIER EST A SON PROPRIETAIRE. Ops peut deposer pour un livreur
        // qui apporte ses papiers au bureau — cela arrivera a Cotonou plus
        // souvent qu'on ne le croit — mais personne d'autre.
        DriverAuthorization.EnsureSelfOrBackOffice(caller, command.DriverId);

        var driver = await drivers.GetByIdAsync(command.DriverId, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException("Livreur", command.DriverId.ToString());

        // LE DOSSIER EST-IL MODIFIABLE ? On le demande AVANT d'ecrire. Ecrire
        // d'abord ferait deposer un objet que le refus suivant rendrait
        // aussitot orphelin — et c'est une piece d'identite.
        if (!driver.DossierModifiable)
        {
            throw new DomainException(
                DriverErrorCodes.ApplicationNotEditable,
                driver.VerificationStatus == VerificationStatus.Suspended
                    ? "Ce compte est suspendu : contactez HBA."
                    : "Ce dossier est valide et ne se modifie plus. Contactez HBA pour le rouvrir.");
        }

        var now = clock.UtcNow;
        var cle = ObjectKeys.Document(driver.Id, command.Type.ToString(), now, command.Extension);

        await objets
            .PutAsync(cle, command.Content, command.SizeBytes, command.ContentType, cancellationToken)
            .ConfigureAwait(false);

        string? remplacee;

        try
        {
            var document = DriverDocument.Create(
                command.Type,
                cle,
                command.ContentType,
                command.SizeBytes,
                now);

            remplacee = driver.AttachDocument(document, caller.ToActor(), now);

            await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // COMPENSATION. Le stockage objet n'est pas transactionnel : si
            // l'enregistrement echoue, l'objet qu'on vient d'ecrire n'a plus
            // aucune ligne pour le reclamer. On le retire, et on laisse
            // remonter la cause d'origine — pas celle du nettoyage.
            await objets.DeleteAsync(cle, CancellationToken.None).ConfigureAwait(false);
            throw;
        }

        // APRES LE COMMIT, ET SEULEMENT APRES. Supprimer d'abord laisserait
        // une ligne qui pointe vers un objet disparu si la transaction
        // echoue. Dans l'autre sens, le pire est un objet orphelin : il
        // occupe du disque, il ne casse rien.
        // ET SEULEMENT SI LA CLE A CHANGE. Elle porte l'horodatage et
        // l'extension, donc elle change en pratique ; ne pas le verifier
        // reviendrait a supprimer l'objet qu'on vient d'ecrire le jour ou
        // deux depots tombent dans la meme seconde avec le meme format.
        if (remplacee is not null && !string.Equals(remplacee, cle, StringComparison.Ordinal))
        {
            await objets.DeleteAsync(remplacee, cancellationToken).ConfigureAwait(false);
        }

        return DriverView.From(driver);
    }
}

public sealed class DeclareVehicleHandler(
    IDriverRepository drivers,
    IUnitOfWork unitOfWork,
    ICallerContext caller,
    IClock clock) : ICommandHandler<DeclareVehicleCommand, DriverView>
{
    public async Task<DriverView> HandleAsync(DeclareVehicleCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        DriverAuthorization.EnsureSelfOrBackOffice(caller, command.DriverId);

        var driver = await drivers.GetByIdAsync(command.DriverId, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException("Livreur", command.DriverId.ToString());

        driver.DeclareVehicle(
            Vehicle.Create(command.Type, command.Plate, command.CapacityGrams),
            caller.ToActor(),
            clock.UtcNow);

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return DriverView.From(driver);
    }
}

public sealed class UploadProfilePhotoHandler(
    IDriverRepository drivers,
    IObjectStore objets,
    IUnitOfWork unitOfWork,
    ICallerContext caller,
    IClock clock) : ICommandHandler<UploadProfilePhotoCommand, DriverView>
{
    public async Task<DriverView> HandleAsync(
        UploadProfilePhotoCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        DriverAuthorization.EnsureSelfOrBackOffice(caller, command.DriverId);

        var driver = await drivers.GetByIdAsync(command.DriverId, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException("Livreur", command.DriverId.ToString());

        // PAS DE CONTROLE DE MODIFIABILITE ICI : la photo de profil se change
        // a tout moment, dossier valide compris (ADR 0021). Elle sert au
        // client a reconnaitre qui arrive, pas a prouver une identite.
        var now = clock.UtcNow;
        var cle = ObjectKeys.ProfilePhoto(driver.Id, now, command.Extension);

        await objets
            .PutAsync(cle, command.Content, command.SizeBytes, command.ContentType, cancellationToken)
            .ConfigureAwait(false);

        string? remplacee;

        try
        {
            remplacee = driver.SetProfilePhoto(cle, caller.ToActor(), now);
            await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await objets.DeleteAsync(cle, CancellationToken.None).ConfigureAwait(false);
            throw;
        }

        // ET SEULEMENT SI LA CLE A CHANGE. Elle porte l'horodatage et
        // l'extension, donc elle change en pratique ; ne pas le verifier
        // reviendrait a supprimer l'objet qu'on vient d'ecrire le jour ou
        // deux depots tombent dans la meme seconde avec le meme format.
        if (remplacee is not null && !string.Equals(remplacee, cle, StringComparison.Ordinal))
        {
            await objets.DeleteAsync(remplacee, cancellationToken).ConfigureAwait(false);
        }

        return DriverView.From(driver);
    }
}

public sealed class SubmitApplicationHandler(
    IDriverRepository drivers,
    IUnitOfWork unitOfWork,
    ICallerContext caller,
    IClock clock) : ICommandHandler<SubmitApplicationCommand, DriverView>
{
    public async Task<DriverView> HandleAsync(SubmitApplicationCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        DriverAuthorization.EnsureSelfOrBackOffice(caller, command.DriverId);

        var driver = await drivers.GetByIdAsync(command.DriverId, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException("Livreur", command.DriverId.ToString());

        // L'AGREGAT REFUSE UN DOSSIER INCOMPLET, et son message nomme les
        // pieces manquantes. Le handler ne revalide rien : deux exemplaires
        // de la meme regle divergent toujours, et c'est celui du domaine qui
        // fait foi.
        driver.SubmitForReview(caller.ToActor(), clock.UtcNow);

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return DriverView.From(driver);
    }
}
