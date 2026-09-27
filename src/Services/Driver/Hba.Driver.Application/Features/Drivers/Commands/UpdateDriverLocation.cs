using Hba.BuildingBlocks.Application.Abstractions;
using Hba.BuildingBlocks.Application.Messaging;
using Hba.Driver.Application.Common.Interfaces;
using Hba.Driver.Domain.ValueObjects;

namespace Hba.Driver.Application.Features.Drivers.Commands;

/// <summary>
/// Rafraichissement de position.
///
/// AUCUNE ECRITURE EN BASE, ET AUCUNE LECTURE NON PLUS. C'est l'appel le plus
/// frequent du systeme — un point toutes les quelques secondes, par livreur en
/// ligne. Y ajouter une lecture de l'agregat pour verifier qu'il a le droit
/// d'etre la multiplierait les requetes Postgres par le nombre de livreurs
/// actifs, pour une verification que la recherche refait de toute facon.
///
/// C'est ce que paie la separation des deux sources : une position qui traine
/// dans Redis sans disponibilite en base ne produit aucune offre, parce que
/// <see cref="Queries.FindAvailableNearbyHandler"/> croise toujours les deux.
/// </summary>
public sealed record UpdateDriverLocationCommand(
    Guid DriverId,
    double Latitude,
    double Longitude,
    DateTimeOffset? CapturedAt) : ICommand<bool>;

public sealed class UpdateDriverLocationHandler(
    IDriverLocationStore locations,
    ICallerContext caller,
    IClock clock) : ICommandHandler<UpdateDriverLocationCommand, bool>
{
    public async Task<bool> HandleAsync(UpdateDriverLocationCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        DriverAuthorization.EnsureSelfOrBackOffice(caller, command.DriverId);

        // L'HORODATAGE VIENT DU TELEPHONE, DONC NE VAUT RIEN POUR LA FRAICHEUR.
        // Une horloge mal reglee, un point mis en file pendant une coupure
        // reseau, et la position paraitrait neuve alors qu'elle a dix minutes
        // — ou l'inverse. Le contrat le transporte pour l'analyse ; le filtre
        // de fraicheur, lui, se fie a l'heure de reception.
        var received = clock.UtcNow;

        await locations
            .UpsertAsync(
                command.DriverId,
                GeoPoint.Create(command.Latitude, command.Longitude),
                received,
                cancellationToken)
            .ConfigureAwait(false);

        return true;
    }
}
