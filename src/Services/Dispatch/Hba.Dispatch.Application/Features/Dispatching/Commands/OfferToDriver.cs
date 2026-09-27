using Hba.BuildingBlocks.Application.Abstractions;
using Hba.BuildingBlocks.Application.Messaging;
using Hba.BuildingBlocks.Domain;
using Hba.BuildingBlocks.Security;
using Hba.Dispatch.Application.Common.Interfaces;
using Hba.Dispatch.Application.Common.Views;
using Hba.Dispatch.Domain.Exceptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Hba.Dispatch.Application.Features.Dispatching.Commands;

/// <summary>
/// L'exploitation propose une course a un livreur qu'elle a choisi.
///
/// POURQUOI CETTE COMMANDE EXISTE. Le moteur cherche par cercles concentriques
/// et par budget de livreurs. Il ne sait pas qu'un tel vient de finir sa
/// course a trois rues du retrait, qu'un autre a appele pour dire qu'il
/// prenait la zone, ou qu'une course urgente doit partir maintenant plutot
/// qu'au prochain balayage. Un humain qui regarde la carte le sait, et jusqu'a
/// present il n'avait aucun moyen de le dire au systeme.
///
/// CE N'EST PAS UNE AFFECTATION. Le livreur recoit une offre ordinaire, avec
/// le meme delai et le meme droit de refus que celles du moteur. Imposer une
/// course a quelqu'un demanderait d'abord de decider ce qui se passe quand il
/// n'est pas d'accord, et cette question n'est pas tranchee.
///
/// PORTEE : LES COURSES QUI CHERCHENT ENCORE. Une course deja acceptee releve
/// de ForceReassign, donc de la politique d'annulation — point 3 des points a
/// trancher, non tranche. Une course en NO_DRIVER_FOUND est un etat TERMINAL
/// de la livraison : la rouvrir n'est pas une fonctionnalite manquante, c'est
/// une regle metier absente du referentiel. Les deux sont refusees ici, avec
/// un code distinct pour que l'ecran ne les confonde pas.
/// </summary>
public sealed record OfferToDriverCommand(
    Guid DeliveryId,
    string DriverId,
    string? Reason) : ICommand<OfferToDriverResult>;

/// <summary>
/// Resultat d'une offre manuelle. <paramref name="RejectionCode"/> porte les
/// motifs de Driver — DRIVER_NOT_FOUND, DRIVER_NOT_VERIFIED,
/// DRIVER_NOT_AVAILABLE, DRIVER_POSITION_STALE — plus DISPATCH_CLOSED et
/// ALREADY_OFFERED.
/// </summary>
public sealed record OfferToDriverResult(bool Sent, string? RejectionCode, OfferView? Offer);

public sealed class OfferToDriverHandler(
    IDispatchRepository dispatches,
    IDriverFinder drivers,
    IUnitOfWork unitOfWork,
    IOptions<DispatchOptions> options,
    ICallerContext caller,
    IClock clock,
    ILogger<OfferToDriverHandler> logger) : ICommandHandler<OfferToDriverCommand, OfferToDriverResult>
{
    private readonly DispatchOptions _options = options.Value;

    public async Task<OfferToDriverResult> HandleAsync(
        OfferToDriverCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        // ADMIN ET OPS, PAS TOUT LE BACK-OFFICE. Le referentiel donne a `ops`
        // « la supervision des livraisons et des livreurs » : placer une
        // course est exactement cela. Le support repond aux clients et la
        // finance lit des montants ; ni l'un ni l'autre n'engage un livreur.
        //
        // VERIFIE ICI, PAS SEULEMENT A LA PASSERELLE, comme l'exige le
        // referentiel : « toute autorisation se verifie cote service ».
        if (!caller.IsInRole(HbaRoles.Admin) && !caller.IsInRole(HbaRoles.Ops))
        {
            throw new ForbiddenException("Seuls admin et ops proposent une course a un livreur.");
        }

        if (string.IsNullOrWhiteSpace(command.DriverId))
        {
            throw new DomainException("MISSING_DRIVER_ID", "Une offre manuelle vise un livreur.");
        }

        var dispatch = await dispatches
            .GetByDeliveryIdAsync(command.DeliveryId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new NotFoundException("Recherche de livreur", command.DeliveryId.ToString());

        // LE REFUS SORT AVANT L'APPEL A DRIVER. Interroger la disponibilite
        // d'un livreur pour une course qui ne cherche plus ferait payer un
        // aller-retour reseau pour un resultat inutilisable, et surtout
        // rendrait « ce livreur n'est pas disponible » la ou la vraie raison
        // est « cette course est close ».
        if (!dispatch.IsOpen)
        {
            return new OfferToDriverResult(false, DispatchErrorCodes.DispatchClosed, null);
        }

        var verdict = await drivers
            .CheckAsync(command.DriverId, dispatch.Pickup, cancellationToken)
            .ConfigureAwait(false);

        if (!verdict.Offerable)
        {
            return new OfferToDriverResult(false, verdict.Reason ?? DispatchErrorCodes.DriverNotAvailable, null);
        }

        var acteur = caller.ToActor();

        try
        {
            var offre = dispatch.OfferTo(
                command.DriverId,
                verdict.DistanceToPickupMeters,
                _options.OfferLifetime,
                acteur,
                clock.UtcNow);

            await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            // LE MOTIF EST JOURNALISE, PAS STOCKE DANS L'AGREGAT. Il n'est pas
            // une donnee de la course : personne, ni le livreur ni le client,
            // ne le lira. Il sert a comprendre APRES COUP pourquoi quelqu'un a
            // court-circuite le moteur, et le journal est fait pour cela.
            logger.LogInformation(
                "Offre manuelle : livraison {DeliveryId} proposee au livreur {DriverId} par {Acteur} " +
                "a {Distance} m du retrait. Motif : {Motif}",
                dispatch.DeliveryId,
                command.DriverId,
                acteur.ToString(),
                verdict.DistanceToPickupMeters,
                string.IsNullOrWhiteSpace(command.Reason) ? "(non precise)" : command.Reason);

            return new OfferToDriverResult(true, null, OfferView.From(dispatch, offre));
        }
        catch (DomainException exception) when (exception.Code is DispatchErrorCodes.AlreadyOffered
                                                    or DispatchErrorCodes.DispatchClosed)
        {
            // DEUX COURSES ENTRE LA LECTURE ET L'ECRITURE : quelqu'un a
            // accepte, ou un second operateur a clique sur le meme livreur.
            // Ni l'un ni l'autre n'est une panne.
            return new OfferToDriverResult(false, exception.Code, null);
        }
    }
}
