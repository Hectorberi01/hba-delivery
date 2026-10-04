using Hba.BuildingBlocks.Application.Abstractions;
using Hba.BuildingBlocks.Application.Messaging;
using Hba.Dispatch.Application.Common.Interfaces;
using Hba.Dispatch.Domain.Dispatching;
using Hba.Dispatch.Domain.ValueObjects;

namespace Hba.Dispatch.Application.Features.Dispatching.Commands;

/// <summary>
/// Ouverture de la recherche pour une mission PAYEE.
///
/// DECLENCHEE PAR UN EVENEMENT DE PAIEMENT CONFIRME, ET PAR RIEN D'AUTRE :
/// « DeliveryConfirmed » pour un colis, « RideDriverSearchStarted » pour une
/// course — celui-la n'est lui-meme emis qu'apres encaissement (point 35).
/// Aucune application ne peut provoquer une recherche.
///
/// REJOUABLE : Kafka livre au moins une fois, et une recherche deja ouverte
/// est rendue telle quelle plutot que doublee — deux moteurs sur la meme
/// mission enverraient deux vagues concurrentes aux memes chauffeurs.
/// </summary>
public sealed record StartDispatchCommand(
    ServiceType ServiceType,
    Guid ReferenceId,
    double PickupLatitude,
    double PickupLongitude,
    string? PickupLandmark,
    int TripDistanceMeters,
    long DriverEarningXof,

    /// <summary>
    /// Le vehicule exige. NON PRECISE VEUT DIRE « N'IMPORTE LEQUEL » : c'est le
    /// cas de toutes les livraisons, dont le contrat ne porte pas le poids du
    /// colis. Une course, elle, le precise toujours.
    /// </summary>
    VehicleType VehicleType = VehicleType.Unspecified) : ICommand<Guid>;

public sealed class StartDispatchHandler(
    IDispatchRepository dispatches,
    IUnitOfWork unitOfWork,
    IClock clock) : ICommandHandler<StartDispatchCommand, Guid>
{
    public async Task<Guid> HandleAsync(StartDispatchCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var existing = await dispatches
            .GetByReferenceAsync(command.ServiceType, command.ReferenceId, cancellationToken)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            return existing.Id;
        }

        var dispatch = DispatchAggregate.Start(
            Guid.CreateVersion7(),
            command.ServiceType,
            command.ReferenceId,
            command.VehicleType,
            GeoPoint.Create(command.PickupLatitude, command.PickupLongitude),
            command.PickupLandmark,
            command.TripDistanceMeters,
            command.DriverEarningXof,
            clock.UtcNow);

        dispatches.Add(dispatch);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // LA PREMIERE VAGUE N'EST PAS ENVOYEE ICI. Elle part au prochain
        // passage du planificateur, qui est aussi celui qui fera suivre les
        // vagues deux et trois. Un seul chemin pour ouvrir une vague, donc une
        // seule logique a tenir juste — au prix de quelques secondes d'attente
        // au demarrage.
        return dispatch.Id;
    }
}
