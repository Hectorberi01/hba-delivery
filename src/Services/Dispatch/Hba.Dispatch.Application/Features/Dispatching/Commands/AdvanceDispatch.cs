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

        // LE PLAFOND SE VERIFIE AVANT DE CHERCHER, PAS APRES AVOIR TROUVE.
        //
        // Interroger Driver pour jeter ensuite le candidat trouve ferait un
        // aller-retour pour rien, et surtout laisserait croire aux journaux
        // qu'un anneau etait vide alors qu'il ne l'etait pas.
        if (dispatch.SolicitedDriverIds.Count >= _options.MaxDriversSolicited)
        {
            dispatch.Exhaust(acteur, now);
            await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            // DEUX RAISONS D'ABANDONNER, DEUX MESSAGES. « Plafond atteint »
            // veut dire que la course a ete refusee par tout le monde : c'est
            // la course qu'il faut regarder — trop loin, mal payee, un
            // quartier qu'on evite. « Perimetre epuise » veut dire qu'il n'y
            // avait personne. Les confondre rendrait les journaux muets sur
            // la seule question qui compte.
            logger.LogInformation(
                "Abandon pour la livraison {DeliveryId} : plafond de {Plafond} livreurs sollicites atteint.",
                dispatch.DeliveryId,
                _options.MaxDriversSolicited);

            return true;
        }

        // ON ELARGIT QUAND L'ANNEAU EST VIDE, PAS QUAND LE COMPTEUR AVANCE.
        //
        // C'ETAIT « UN RAYON PAR VAGUE », ET CELA BORNAIT LA RECHERCHE A
        // TROIS LIVREURS. Le rayon se lisait dans le tableau a l'indice de la
        // vague ; passe la troisieme, il n'y avait plus de rayon et la course
        // basculait en NO_DRIVER_FOUND — meme si douze livreurs attendaient a
        // deux kilometres et qu'aucun n'avait ete sollicite. Le nombre de
        // tentatives etait donc borne par la longueur d'un tableau de
        // distances, ce que rien ne justifiait.
        //
        // DESORMAIS : on demande au premier anneau qui rend encore quelqu'un.
        // Les deja sollicites sont exclus cote Driver, donc un anneau
        // « epuise » ne rend plus rien et l'on passe au suivant tout seul. La
        // recherche s'arrete quand le PLUS LARGE des anneaux est vide — la
        // liste borne la recherche, plus le compteur.
        foreach (var rayon in _options.WaveRadiiMeters)
        {
            // L'EXCLUSION EST ENVOYEE A DRIVER, PAS FILTREE APRES : demander
            // des livreurs puis en jeter la moitie deja sollicites rendrait
            // des vagues plus maigres que demande. Le contrat prevoit
            // « exclude_driver_ids » exactement pour ca.
            var candidats = await drivers
                .FindAvailableNearbyAsync(
                    dispatch.Pickup,
                    rayon,
                    _options.DriversPerWave,
                    [.. dispatch.SolicitedDriverIds],
                    cancellationToken)
                .ConfigureAwait(false);

            if (candidats.Count == 0)
            {
                continue;
            }

            var offres = dispatch.OpenWave(candidats, _options.OfferLifetime, acteur, now);

            await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            logger.LogInformation(
                "Tentative {Vague} pour la livraison {DeliveryId} : {Offres} offre(s) dans un rayon de {Rayon} m.",
                prochaine,
                dispatch.DeliveryId,
                offres.Count,
                rayon);

            return true;
        }

        // Aucun anneau n'a rendu quelqu'un : tout le perimetre a ete sollicite,
        // ou il est desert.
        dispatch.Exhaust(acteur, now);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "Aucun livreur pour la livraison {DeliveryId} apres {Tentatives} tentative(s), "
                + "perimetre de {Rayon} m epuise.",
            dispatch.DeliveryId,
            dispatch.CurrentWave,
            _options.WaveRadiiMeters.Length > 0
                ? _options.WaveRadiiMeters[^1]
                : 0);

        return true;
    }
}
