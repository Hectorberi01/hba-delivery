using Hba.BuildingBlocks.Application.Abstractions;
using Hba.BuildingBlocks.Application.Messaging;
using Hba.Dispatch.Application.Common.Interfaces;
using Hba.Dispatch.Domain.Dispatching;
using Hba.Dispatch.Domain.ValueObjects;

namespace Hba.Dispatch.Application.Features.Dispatching.Commands;

/// <summary>
/// Ouverture de la recherche pour une livraison payee.
///
/// DECLENCHEE PAR « DeliveryConfirmed », ET PAR RIEN D'AUTRE. Le flux le dit :
/// « C'est le seul declencheur de la recherche de livreur. » Aucune
/// application ne peut la provoquer.
///
/// REJOUABLE : Kafka livre au moins une fois, et une recherche deja ouverte
/// est rendue telle quelle plutot que doublee — deux moteurs sur la meme
/// course enverraient deux vagues concurrentes aux memes livreurs.
/// </summary>
public sealed record StartDispatchCommand(
    Guid DeliveryId,
    double PickupLatitude,
    double PickupLongitude,
    string? PickupLandmark,
    int TripDistanceMeters,
    long DriverEarningXof) : ICommand<Guid>;

public sealed class StartDispatchHandler(
    IDispatchRepository dispatches,
    IUnitOfWork unitOfWork,
    IClock clock) : ICommandHandler<StartDispatchCommand, Guid>
{
    public async Task<Guid> HandleAsync(StartDispatchCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var existing = await dispatches
            .GetByDeliveryIdAsync(command.DeliveryId, cancellationToken)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            return existing.Id;
        }

        var dispatch = DispatchAggregate.Start(
            Guid.CreateVersion7(),
            command.DeliveryId,
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
