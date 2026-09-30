using Hba.BuildingBlocks.Application.Abstractions;
using Hba.BuildingBlocks.Application.Messaging;
using Hba.Directory.Application.Ports;
using Microsoft.Extensions.Logging;

namespace Hba.Directory.Application.Customers;

/// <summary>
/// Efface la fiche d'un client dont le compte vient d'être supprimé.
/// </summary>
///
/// <remarks>
/// ELLE N'EST DECLENCHEE QUE PAR UN EVENEMENT D'IDENTITY, jamais par une route.
/// Une fiche client ne se supprime pas depuis le back-office : elle disparaît
/// avec le compte de son titulaire, qui est le seul à pouvoir le demander.
///
/// CE QUE CETTE COMMANDE EMPORTE : le nom, le téléphone, le courriel, les
/// adresses favorites — avec les noms et téléphones de TIERS que le client y
/// avait enregistrés — et la photo de profil, par Media.
///
/// CE QU'ELLE N'EMPORTE PAS, ET POURQUOI : les courses restent chez Delivery.
/// C'est écrit dans la politique de confidentialité que le client a lue —
/// « les courses déjà effectuées restent dans l'historique de HBA, elles
/// concernent aussi des livreurs et des destinataires, mais votre nom en est
/// retiré ». Il se trouve que Delivery ne détient PAS le nom du client : il ne
/// garde que son identifiant, qui ne résout plus rien une fois Identity et
/// Directory effacés. Il n'y a donc rien à y anonymiser.
///
/// Payment et Notification ne sont pas touchés non plus : voir le point 28 des
/// points à trancher, qui dit pourquoi et ce qu'il reste à vérifier.
///
/// ELLE EST IDEMPOTENTE. Kafka livre au moins une fois ; une fiche déjà partie
/// n'est pas une erreur, et lever ici ferait rejouer le message indéfiniment.
/// </remarks>
public sealed record EraseCustomerCommand(Guid CustomerId) : ICommand<Unit>;

public sealed class EraseCustomerHandler(
    ICustomerRepository customers,
    IMediaCatalogue media,
    IUnitOfWork unitOfWork,
    ILogger<EraseCustomerHandler> journal) : ICommandHandler<EraseCustomerCommand, Unit>
{
    public async Task<Unit> HandleAsync(EraseCustomerCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var customer = await customers
            .GetByIdAsync(command.CustomerId, cancellationToken)
            .ConfigureAwait(false);

        if (customer is null)
        {
            journal.LogInformation(
                "Aucune fiche pour le compte efface {CustomerId} : rien a supprimer.",
                command.CustomerId);

            return Unit.Value;
        }

        // LA PHOTO PART AVANT LA FICHE, ET L'ORDRE COMPTE. Media range ce qui
        // appartient a un client sous un prefixe tire de son identifiant, donc
        // l'appel n'a besoin que de lui — mais si la fiche partait d'abord et
        // que ce service tombait juste apres, plus rien dans Directory ne
        // dirait qu'il reste une photo a effacer.
        //
        // DeleteOwnerMedia EMPORTE TOUT CE QUE LE CLIENT POSSEDE, pas seulement
        // son portrait : c'est la moitie manquante de la suppression de compte
        // que le point 22 laissait ouverte faute de pouvoir enumerer les
        // objets du stockage.
        await media
            .SupprimerParProprietaireAsync(command.CustomerId, cancellationToken)
            .ConfigureAwait(false);

        customers.Remove(customer);

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // « WARNING » POUR UNE OPERATION NORMALE, comme cote Identity : un
        // effacement est irreversible et concerne une personne. Le jour ou
        // quelqu'un demandera ce qu'est devenue sa fiche, cette ligne est la
        // seule chose qui restera pour repondre.
        journal.LogWarning("Fiche du client {CustomerId} effacee definitivement.", command.CustomerId);

        return Unit.Value;
    }
}
