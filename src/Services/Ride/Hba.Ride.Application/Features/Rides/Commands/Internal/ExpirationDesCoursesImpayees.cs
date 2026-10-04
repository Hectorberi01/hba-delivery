using Hba.BuildingBlocks.Application.Abstractions;
using Hba.BuildingBlocks.Application.Messaging;
using Hba.BuildingBlocks.Domain;
using Hba.Ride.Application.Ports;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Hba.Ride.Application.Features.Rides.Commands.Internal;

/// <summary>
/// Le temps laissé au passager pour payer, au-delà duquel la course est abandonnée.
/// </summary>
///
/// <remarks>
/// QUINZE MINUTES, COMME POUR LES COLIS, ET POUR LA MÊME RAISON : c'est la durée
/// de vie d'un devis (Pricing, <c>GetQuote.Lifetime</c>). Au-delà, le prix figé
/// n'engage plus personne, et une course ouverte plus longtemps que son propre
/// prix ne veut rien dire.
///
/// UN DÉLAI PLUS COURT A ÉTÉ ÉCARTÉ le 2 octobre 2026. L'argument pour était
/// qu'un passager attend un chauffeur MAINTENANT ; l'argument contre a gagné :
/// un paiement Mobile Money se saisit lentement — code marchand, montant, code
/// secret, confirmation — et fermer à cinq minutes aurait repris la course de
/// quelqu'un en train de payer. Deux délais à expliquer pour un gain que
/// personne ne ressent.
///
/// LE DEVIS N'EST PAS RENDU, ET IL N'Y A RIEN À RENDRE : à l'instant où la course
/// expire, il a expiré de son côté. Un passager qui reprend en demande un
/// nouveau, au prix du moment.
/// </remarks>
public sealed class UnpaidRideOptions
{
    public const string Section = "UnpaidRide";

    /// <summary>Délai accordé au paiement, en minutes.</summary>
    public int GraceMinutes { get; set; } = 15;

    /// <summary>Intervalle entre deux passages du balayage.</summary>
    public int SweepMinutes { get; set; } = 1;

    /// <summary>Nombre maximal de courses abandonnées par passage.</summary>
    ///
    /// <remarks>
    /// UN PLAFOND, PARCE QUE CHAQUE ABANDON PUBLIE UN ÉVÉNEMENT. Après un arrêt
    /// prolongé du service, la totalité des impayées partirait d'un coup dans
    /// l'Outbox. Le passage suivant reprend la suite.
    /// </remarks>
    public int BatchSize { get; set; } = 100;
}

public sealed record ExpireUnpaidRidesCommand : ICommand<int>;

/// <summary>
/// Abandonne les courses qu'aucun paiement n'est venu confirmer.
/// </summary>
///
/// <remarks>
/// CE BALAYAGE N'EXISTAIT PAS, ET C'EST UN TROU QUE LE POINT 30 N'A JAMAIS
/// COUVERT. Il a été tranché le 29 septembre 2026, deux jours avant que Ride ne
/// soit écrit : la décision parlait de livraisons, et le service des courses est
/// né sans son équivalent. Une course abandonnée au paiement restait en
/// <c>Requested</c> pour toujours, son devis consommé et son intention ouverte.
///
/// LE DOMAINE GARDE LA MAIN. Ce gestionnaire ne décide de rien : c'est
/// <c>MarkPaymentFailed</c> qui vérifie la transition, et la table des
/// transitions qui autorise <c>Requested → PaymentFailed</c>.
///
/// LE REFUS NE FAIT PAS ÉCHOUER LE PASSAGE. Une course qui a changé d'état entre
/// la lecture et l'écriture — un webhook arrivé entre-temps, le cas même qu'on
/// espère — se refuse, et c'est normal : on la laisse où elle est et on continue.
/// Lever ici arrêterait le balayage des quatre-vingt-dix-neuf autres.
/// </remarks>
public sealed class ExpireUnpaidRidesHandler(
    IRideRepository rides,
    IUnitOfWork unitOfWork,
    IClock clock,
    IOptions<UnpaidRideOptions> options,
    ILogger<ExpireUnpaidRidesHandler> journal)
    : ICommandHandler<ExpireUnpaidRidesCommand, int>
{
    public async Task<int> HandleAsync(
        ExpireUnpaidRidesCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(options);

        var maintenant = clock.UtcNow;
        var limite = maintenant.AddMinutes(-Math.Max(1, options.Value.GraceMinutes));

        var abandonnees = await rides
            .ListUnpaidBeforeAsync(limite, options.Value.BatchSize, cancellationToken)
            .ConfigureAwait(false);

        if (abandonnees.Count == 0)
        {
            return 0;
        }

        var fermees = 0;

        foreach (var course in abandonnees)
        {
            var resultat = course.MarkPaymentFailed(
                "Paiement non confirmé dans le délai imparti.",
                Actor.Scheduler,
                maintenant);

            if (resultat.Succeeded)
            {
                fermees++;
            }
        }

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        journal.LogInformation(
            "{Nombre} course(s) de personnes abandonnee(s) faute de paiement confirme.",
            fermees);

        return fermees;
    }
}
