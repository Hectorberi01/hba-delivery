using Hba.BuildingBlocks.Application.Abstractions;
using Hba.BuildingBlocks.Application.Messaging;
using Hba.BuildingBlocks.Domain;
using Hba.Dispatch.Application.Common.Interfaces;
using Hba.Dispatch.Domain.Dispatching;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Hba.Dispatch.Application.Features.Dispatching.Commands;

/// <summary>
/// Fait avancer UNE recherche d'un cran : eteindre ce qui a expire, puis
/// ouvrir la vague suivante ou declarer l'echec.
///
/// C'EST LE SEUL ENDROIT QUI OUVRE UNE VAGUE. La premiere comme la troisieme
/// passent par ici, appelees par le planificateur — l'acteur que le
/// referentiel designe pour les « timeouts d'offre ». Une seule logique a
/// tenir juste, au lieu d'un chemin pour le demarrage et d'un autre pour la
/// suite, qui divergeraient au premier correctif.
/// </summary>
public sealed record AdvanceDispatchCommand(Guid DeliveryId) : ICommand<bool>;

public sealed class AdvanceDispatchHandler(
    IDispatchRepository dispatches,
    IDriverFinder drivers,
    IUnitOfWork unitOfWork,
    IOptions<DispatchOptions> options,
    IClock clock,
    ILogger<AdvanceDispatchHandler> logger) : ICommandHandler<AdvanceDispatchCommand, bool>
{
    private readonly DispatchOptions _options = options.Value;

    public async Task<bool> HandleAsync(AdvanceDispatchCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var dispatch = await dispatches
            .GetByDeliveryIdAsync(command.DeliveryId, cancellationToken)
            .ConfigureAwait(false);

        if (dispatch is null || !dispatch.IsOpen)
        {
            return false;
        }

        var now = clock.UtcNow;
        var acteur = Actor.Scheduler;

        var eteintes = dispatch.ExpireDueOffers(acteur, now);

        if (!dispatch.WaveIsSettled(_options.OfferLifetime, now))
        {
            // Des offres courent encore : on laisse le delai s'ecouler.
            if (eteintes > 0)
            {
                await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }

            return false;
        }

        var prochaine = dispatch.CurrentWave + 1;
        var rayon = _options.RadiusForWave(prochaine);

        if (rayon is null)
        {
            dispatch.Exhaust(acteur, now);
            await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            logger.LogInformation(
                "Aucun livreur pour la livraison {DeliveryId} apres {Vagues} vagues.",
                dispatch.DeliveryId,
                dispatch.CurrentWave);

            return true;
        }

        // L'EXCLUSION EST ENVOYEE A DRIVER, PAS FILTREE APRES : demander cinq
        // livreurs puis en jeter trois deja sollicites rendrait des vagues de
        // deux. Le contrat prevoit « exclude_driver_ids » exactement pour ca.
        var candidats = await drivers
            .FindAvailableNearbyAsync(
                dispatch.Pickup,
                rayon.Value,
                _options.DriversPerWave,
                [.. dispatch.SolicitedDriverIds],
                cancellationToken)
            .ConfigureAwait(false);

        var offres = dispatch.OpenWave(candidats, _options.OfferLifetime, acteur, now);

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "Vague {Vague} pour la livraison {DeliveryId} : {Offres} offre(s) dans un rayon de {Rayon} m.",
            prochaine,
            dispatch.DeliveryId,
            offres.Count,
            rayon.Value);

        return true;
    }
}
