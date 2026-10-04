using Hba.BuildingBlocks.Application.Messaging;
using Hba.BuildingBlocks.Messaging.Kafka;
using Hba.Contracts.Ride.V1;
using Hba.Payment.Application.Features.Earnings.Commands;
using Hba.Payment.Application.Features.Refunds.Commands;
using Hba.Payment.Domain.Payments;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Hba.Payment.Api.Messaging;

/// <summary>
/// Ce que les courses de personnes changent cote argent : le gain du chauffeur
/// quand elles aboutissent, un remboursement a faire quand elles n'aboutissent pas.
/// </summary>
///
/// <remarks>
/// SANS CE CONSOMMATEUR, UN CHAUFFEUR ENCHAINAIT LES COURSES ET SON RELEVE
/// RESTAIT VIDE : le grand livre ne connaissait que la livraison, donc il ne
/// pouvait rien retirer de ce qu'il avait gagne a transporter des gens.
///
/// SEULE LA COURSE TERMINEE CREDITE, comme pour un colis : une course annulee ou
/// sans chauffeur ne rapporte rien. Si un dedommagement doit exister un jour, ce
/// sera une regle a trancher et une autre nature de mouvement, pas un
/// « presque terminee » glisse ici.
///
/// LE MONTANT EST LA PART DU CHAUFFEUR, PAS LE PRIX PAYE. L'evenement porte les
/// deux, et les confondre donnerait au chauffeur la totalite de la course : la
/// commission de HBA est l'ecart entre les deux, deja prise dans le devis.
/// </remarks>
public sealed class RideEventsConsumer(
    IServiceScopeFactory scopeFactory,
    IOptions<KafkaOptions> options,
    ILogger<RideEventsConsumer> logger)
    : KafkaConsumerBase<RideEvent>(scopeFactory, options, logger)
{
    protected override string Topic => KafkaTopics.RideEvents;

    protected override Guid GetEventId(RideEvent message)
    {
        ArgumentNullException.ThrowIfNull(message);
        return Guid.TryParse(message.Envelope?.EventId, out var id) ? id : Guid.CreateVersion7();
    }

    protected override string GetEventType(RideEvent message)
    {
        ArgumentNullException.ThrowIfNull(message);
        return message.Envelope?.EventType ?? "unknown";
    }

    protected override async Task HandleAsync(
        RideEvent message,
        IServiceProvider services,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(services);

        var dispatcher = services.GetRequiredService<IDispatcher>();

        switch (message.PayloadCase)
        {
            case RideEvent.PayloadOneofCase.Completed:
                await CrediterAsync(dispatcher, message.Completed, cancellationToken).ConfigureAwait(false);
                break;

            // AUCUN CHAUFFEUR TROUVE : le passager a paye et n'a RIEN eu. Tout est
            // du, et aucune regle ne manque pour le dire.
            case RideEvent.PayloadOneofCase.NoDriverAvailable:
                await RembourserAsync(
                    dispatcher,
                    message.NoDriverAvailable.RideId,
                    "RIDE_NO_DRIVER",

                    // PERSONNE NE S'EST DEPLACE : rien n'est retenu, tout est du.
                    retenueXof: 0,
                    message.NoDriverAvailable.OccurredAt?.ToDateTimeOffset() ?? DateTimeOffset.UtcNow,
                    cancellationToken).ConfigureAwait(false);
                break;

            // ANNULEE : ce qui est du depend de ce qui s'etait deja passe, et la
            // retenue le dit maintenant en toutes lettres.
            //
            // IL N'Y A PLUS DE « A DETERMINER » DEPUIS LE 2 OCTOBRE 2026. Le point 3
            // etait non tranche : avec un chauffeur deja en route, la ligne de
            // remboursement s'inscrivait sans montant recommande et la finance
            // inventait un chiffre, course par course. La retenue est desormais figee
            // dans le devis et voyage dans l'evenement ; le recommande se calcule.
            case RideEvent.PayloadOneofCase.Cancelled:

                // DEDOMMAGER AVANT DE REMBOURSER, ET L'ORDRE N'EST PAS INDIFFERENT.
                // Les deux ecritures viennent du meme chiffre : si la seconde echoue,
                // la file de remboursement manque une ligne que la finance verra
                // manquer. Si c'etait la premiere, un chauffeur ne serait pas paye et
                // PERSONNE ne le verrait — un releve n'a pas de trou visible.
                await DedommagerAsync(
                    dispatcher,
                    message.Cancelled.RideId,
                    message.Cancelled.DriverId,
                    message.Cancelled.CancellationFee?.Amount ?? 0,
                    message.Cancelled.OccurredAt?.ToDateTimeOffset() ?? DateTimeOffset.UtcNow,
                    cancellationToken).ConfigureAwait(false);

                await RembourserAsync(
                    dispatcher,
                    message.Cancelled.RideId,
                    "RIDE_CANCELLED",
                    retenueXof: message.Cancelled.CancellationFee?.Amount ?? 0,
                    message.Cancelled.OccurredAt?.ToDateTimeOffset() ?? DateTimeOffset.UtcNow,
                    cancellationToken).ConfigureAwait(false);
                break;

            default:
                break;
        }
    }

    private static async Task CrediterAsync(
        IDispatcher dispatcher,
        RideCompleted terminee,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(terminee.RideId, out var rideId))
        {
            return;
        }

        await dispatcher
            .SendAsync(
                new RecordMissionEarningCommand(
                    ServiceType.Ride,
                    rideId,
                    terminee.DriverId,

                    // UNE COURSE N'A PAS DE REFERENCE LISIBLE : Ride n'en attribue
                    // pas, contrairement a Delivery. Le releve retombe sur
                    // l'identifiant — moins lisible, mais vrai. Fabriquer un numero
                    // ici en inventerait un que personne d'autre ne connaitrait.
                    string.Empty,

                    terminee.DriverEarning?.Amount ?? 0,
                    terminee.OccurredAt?.ToDateTimeOffset() ?? DateTimeOffset.UtcNow),
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Inscrit un remboursement a faire, si l'argent est bien arrive.
    /// </summary>
    ///
    /// <remarks>
    /// LA COMMANDE VERIFIE ELLE-MEME QUE LE PAIEMENT A REUSSI, et c'est pour cela
    /// qu'on l'appelle sans condition : une course annulee avant paiement ne produit
    /// aucune ligne. Faire ce test ici obligerait ce consommateur a connaitre l'etat
    /// des intentions, ce qui n'est pas son affaire.
    ///
    /// <paramref name="retenueXof"/> EST CE QUI NE SERA PAS RENDU : la part qui
    /// reste au chauffeur. Zero veut dire que tout l'encaissement est du.
    /// </remarks>
    private static async Task RembourserAsync(
        IDispatcher dispatcher,
        string rideId,
        string motif,
        long retenueXof,
        DateTimeOffset quand,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(rideId, out var id))
        {
            return;
        }

        await dispatcher
            .SendAsync(
                new OpenRefundDueCommand(ServiceType.Ride, id, motif, retenueXof, quand),
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Credite le chauffeur de ce que lui laisse une course qui n'a pas abouti.
    /// </summary>
    ///
    /// <remarks>
    /// APPELEE SANS CONDITION, COMME LE REMBOURSEMENT : la commande sait seule si la
    /// retenue vaut quelque chose, et c'est elle qui sort sans ecrire quand elle est
    /// nulle. Tester ici obligerait ce consommateur a connaitre la politique
    /// d'annulation, ce qui n'est pas son affaire.
    ///
    /// AUCUNE REFERENCE LISIBLE, comme pour la remuneration d'une course terminee :
    /// Ride n'attribue pas de numero, contrairement a Delivery. Le releve retombe sur
    /// l'identifiant.
    /// </remarks>
    private static async Task DedommagerAsync(
        IDispatcher dispatcher,
        string rideId,
        string driverId,
        long retenueXof,
        DateTimeOffset quand,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(rideId, out var id))
        {
            return;
        }

        await dispatcher
            .SendAsync(
                new RecordMissionCompensationCommand(
                    ServiceType.Ride,
                    id,
                    driverId,
                    string.Empty,
                    retenueXof,
                    quand),
                cancellationToken)
            .ConfigureAwait(false);
    }
}
