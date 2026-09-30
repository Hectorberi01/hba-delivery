using Hba.BuildingBlocks.Application.Abstractions;
using Hba.BuildingBlocks.Application.Messaging;
using Hba.BuildingBlocks.Domain;
using Hba.BuildingBlocks.Security;
using Hba.Delivery.Application.Ports;

namespace Hba.Delivery.Application.Commands.DriverActions;

/// <summary>
/// Le dépôt d'une preuve : Delivery autorise, puis porte les octets.
/// </summary>
///
/// <remarks>
/// IL N'HERITE PAS DE <c>DriverActionHandlerBase</c>, ET CE N'EST PAS UN OUBLI.
/// Le socle applique une fonction SYNCHRONE à l'agrégat, entre le chargement et
/// la sauvegarde ; ici il faut un appel réseau AU MILIEU — le dépôt chez Media —
/// et le glisser dans ce moule obligerait soit à rendre le socle asynchrone pour
/// un seul appelant, soit à déposer avant d'avoir chargé la course, c'est-à-dire
/// avant de savoir si on a le droit. Les contrôles que le socle fait sont donc
/// refaits ici, à l'identique et dans le même ordre.
///
/// L'ORDRE DES TROIS TEMPS EST LA SEULE CHOSE QUI COMPTE VRAIMENT :
/// <list type="number">
/// <item>ON DEMANDE A L'AGREGAT S'IL PEUT RECEVOIR, sans rien modifier. Sans ce
/// contrôle avancé, une photo de trois cents kilo-octets partirait chez Media
/// avant qu'on découvre que l'étape n'a pas eu lieu : le livreur aurait payé ses
/// données pour un refus, et Media garderait un fichier que plus rien ne
/// réclame.</item>
/// <item>ON DEPOSE CHEZ MEDIA, et l'identifiant qui revient désigne un fichier
/// qui existe. C'est toute la différence avec ce que le service refuse encore
/// sous <c>PROOF_NOT_SUPPORTED</c> : cette clé-là venait du téléphone et ne
/// nommait rien.</item>
/// <item>ON ECRIT SUR L'AGREGAT, ET ON ENREGISTRE.</item>
/// </list>
///
/// CET ORDRE LAISSE UNE FENETRE, ET C'EST LA MOINS MAUVAISE. Si la sauvegarde
/// échoue après le dépôt, il reste chez Media un objet que la course ne réclame
/// pas — un orphelin. La purge des preuves le balaie au bout de trente jours,
/// parce qu'il EST dans l'inventaire. L'ordre inverse, lui, écrirait sur la
/// course l'identifiant d'un fichier qui n'existe pas encore et pourrait ne
/// jamais exister : une preuve invérifiable, c'est-à-dire exactement ce que le
/// service refuse par ailleurs. Entre un fichier de trop qui s'efface tout seul
/// et une preuve qui ment, le choix n'est pas serré.
/// </remarks>
public sealed class AttacherLaPreuveHandler(
    IDeliveryRepository repository,
    IDepotDePreuve depot,
    IUnitOfWork unitOfWork,
    ICallerContext caller,
    IClock clock) : ICommandHandler<AttacherLaPreuveCommand, PreuveDeposee>
{
    public async Task<PreuveDeposee> HandleAsync(
        AttacherLaPreuveCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!caller.IsInRole(HbaRoles.Driver))
        {
            throw new ForbiddenException("Cette action appartient au livreur affecté.");
        }

        var delivery = await repository.GetByIdAsync(command.DeliveryId, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException("Livraison", command.DeliveryId.ToString());

        // NotFound et non Forbidden : ne rien révéler d'une course qui n'est pas
        // la sienne. Même choix que le socle des actions du livreur.
        if (delivery.Driver is null || delivery.Driver.DriverId != caller.DriverId)
        {
            throw new NotFoundException("Livraison", command.DeliveryId.ToString());
        }

        var actor = caller.ToActor();

        // L'HEURE EST CELLE DU SERVEUR, ET LA COMMANDE N'EN PORTE PAS D'AUTRE.
        // Les quatre autres actions du livreur acceptent l'horodatage du
        // telephone parce qu'elles se rejouent depuis la file hors ligne ;
        // une photo n'entre jamais dans cette file — c'est le fait meme qui a
        // ecarte « photo exigee ». Un depot se fait donc toujours en direct, et
        // laisser le telephone dater ce qu'il envoie a l'instant lui donnerait
        // le moyen de contourner la fenetre de depot en se declarant
        // a l'heure.
        var maintenant = clock.UtcNow;

        var deja = delivery.EnsurePeutRecevoirLaPreuve(command.Etape, actor, maintenant);

        if (deja is not null)
        {
            // ON REFUSE AVANT DE DEPOSER. L'agrégat refuserait de toute façon,
            // mais après que les octets ont traversé le réseau et occupé le
            // stockage : un refus qui coûte un fichier n'est pas un refus.
            throw new DomainException(
                "PROOF_ALREADY_ATTACHED",
                "Une preuve est déjà jointe à cette étape, et elle ne se remplace pas.");
        }

        var mediaId = await depot
            .DeposerAsync(
                delivery.Id,
                command.Contenu,
                command.TailleOctets,
                command.TypeDeContenu,
                cancellationToken)
            .ConfigureAwait(false);

        delivery.AttacherLaPreuve(command.Etape, mediaId, actor, maintenant);

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new PreuveDeposee(mediaId, command.Etape);
    }
}
