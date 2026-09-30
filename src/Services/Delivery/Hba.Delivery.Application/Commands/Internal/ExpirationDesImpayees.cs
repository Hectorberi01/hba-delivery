using Hba.BuildingBlocks.Application.Abstractions;
using Hba.BuildingBlocks.Application.Messaging;
using Hba.BuildingBlocks.Domain;
using Hba.Delivery.Application.Ports;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Hba.Delivery.Application.Commands.Internal;

/// <summary>
/// Le temps laissé au client pour payer, au-delà duquel la course est abandonnée.
/// </summary>
///
/// <remarks>
/// QUINZE MINUTES, ET CE N'EST PAS UN CHIFFRE EN L'AIR : c'est exactement la
/// durée de vie d'un devis (Pricing, <c>GetQuote.Lifetime</c>). Au-delà, le prix
/// figé n'engage plus personne ; laisser la course ouverte plus longtemps que
/// son propre prix n'aurait rien voulu dire.
///
/// LE DEVIS N'EST PAS RENDU, ET IL N'Y A RIEN A RENDRE. À l'instant où la course
/// expire, le devis a expiré de son côté. Un client qui reprend en demande un
/// nouveau, au prix du moment — ce qui est la seule chose honnête, puisque le
/// prix d'il y a un quart d'heure n'est plus garanti par Pricing.
/// </remarks>
public sealed class UnpaidDeliveryOptions
{
    public const string Section = "UnpaidDelivery";

    /// <summary>Délai accordé au paiement, en minutes.</summary>
    public int GraceMinutes { get; set; } = 15;

    /// <summary>Intervalle entre deux passages du balayage.</summary>
    public int SweepMinutes { get; set; } = 1;

    /// <summary>Nombre maximal de courses abandonnées par passage.</summary>
    ///
    /// <remarks>
    /// UN PLAFOND, PARCE QUE CHAQUE ABANDON PUBLIE UN EVENEMENT. Après un arrêt
    /// prolongé du service, la totalité des impayées partirait d'un coup dans
    /// l'Outbox. Le passage suivant reprend la suite.
    /// </remarks>
    public int BatchSize { get; set; } = 100;
}

public sealed record ExpireUnpaidDeliveriesCommand : ICommand<int>;

/// <summary>
/// Abandonne les courses qu'aucun paiement n'est venu confirmer.
/// </summary>
///
/// <remarks>
/// SEULES LES COURSES QUI ATTENDENT UN PAIEMENT FEDAPAY SONT CONCERNEES, et
/// c'est la condition « une intention de paiement est rattachée » qui le dit.
/// Une course créée par un PARTENAIRE n'en reçoit aucune — son règlement est un
/// point non tranché, voir points-a-trancher.md — et doit rester où elle est.
/// Balayer sur le seul statut effacerait toutes les commandes B2B un quart
/// d'heure après leur création.
///
/// LE DOMAINE GARDE LA MAIN. Ce gestionnaire ne décide de rien : c'est
/// <c>FailPayment</c> qui vérifie la transition et l'acteur, et la table des
/// transitions qui autorise le planificateur à le faire depuis PENDING_PAYMENT
/// et depuis nulle part ailleurs.
/// </remarks>
public sealed class ExpireUnpaidDeliveriesHandler(
    IDeliveryRepository repository,
    IUnitOfWork unitOfWork,
    IClock clock,
    IOptions<UnpaidDeliveryOptions> options,
    ILogger<ExpireUnpaidDeliveriesHandler> journal)
    : ICommandHandler<ExpireUnpaidDeliveriesCommand, int>
{
    public async Task<int> HandleAsync(
        ExpireUnpaidDeliveriesCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(options);

        var now = clock.UtcNow;
        var limite = now.AddMinutes(-Math.Max(1, options.Value.GraceMinutes));

        var abandonnees = await repository
            .ListUnpaidBeforeAsync(limite, options.Value.BatchSize, cancellationToken)
            .ConfigureAwait(false);

        if (abandonnees.Count == 0)
        {
            return 0;
        }

        foreach (var delivery in abandonnees)
        {
            delivery.FailPayment(
                "Paiement non confirmé dans le délai imparti.",
                Actor.Scheduler,
                now);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        journal.LogInformation(
            "{Nombre} course(s) abandonnee(s) faute de paiement confirme.",
            abandonnees.Count);

        return abandonnees.Count;
    }
}
