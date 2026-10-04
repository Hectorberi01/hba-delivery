using Hba.BuildingBlocks.Application.Messaging;
using Hba.BuildingBlocks.Messaging.Kafka;
using Hba.Contracts.Delivery.V1;
using Hba.Payment.Application.Features.Earnings.Commands;
using Hba.Payment.Application.Features.Refunds.Commands;
using Hba.Payment.Domain.Payments;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Hba.Payment.Api.Messaging;

/// <summary>
/// Une course livree credite le compte de son livreur.
///
/// POURQUOI PAYMENT, ET PAS DRIVER NI DELIVERY. Le referentiel confie a
/// « finance » les paiements, les remboursements ET les reversements : les
/// trois sont le meme metier, traite par la meme personne, et Payment les
/// tient deja tous les deux premiers. Driver sait qui roule, pas ce qu'on lui
/// doit ; Delivery sait ce qu'une course vaut, pas ce qui reste a verser.
///
/// PREMIER CONSOMMATEUR DE CE SERVICE. Payment ne faisait jusqu'ici que
/// publier — il ecoute maintenant, et son groupe de consommateurs n'ayant
/// jamais lu ce sujet, il rattrape l'historique depuis l'origine. C'est
/// voulu : les courses deja livrees doivent apparaitre au compte de leur
/// livreur, pas seulement les prochaines.
/// </summary>
public sealed class DeliveryEventsConsumer(
    IServiceScopeFactory scopeFactory,
    IOptions<KafkaOptions> options,
    ILogger<DeliveryEventsConsumer> logger)
    : KafkaConsumerBase<DeliveryEvent>(scopeFactory, options, logger)
{
    protected override string Topic => KafkaTopics.DeliveryEvents;

    protected override Guid GetEventId(DeliveryEvent message)
    {
        ArgumentNullException.ThrowIfNull(message);
        return Guid.TryParse(message.Envelope?.EventId, out var id) ? id : Guid.CreateVersion7();
    }

    protected override string GetEventType(DeliveryEvent message)
    {
        ArgumentNullException.ThrowIfNull(message);
        return message.Envelope?.EventType ?? "unknown";
    }

    protected override async Task HandleAsync(
        DeliveryEvent message,
        IServiceProvider services,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(services);

        var dispatcher = services.GetRequiredService<IDispatcher>();

        switch (message.PayloadCase)
        {
            // SEULE LA REMISE CREDITE. Une livraison annulee ou echouee ne rapporte
            // rien : si un dedommagement doit exister un jour, ce sera une regle a
            // trancher et une autre nature de mouvement, pas un « presque livree »
            // glisse ici.
            case DeliveryEvent.PayloadOneofCase.Delivered:
                await CrediterAsync(dispatcher, message.Delivered, cancellationToken).ConfigureAwait(false);
                break;

            // AUCUN LIVREUR TROUVE : l'expediteur a paye et son colis n'a pas bouge.
            // Tout est du, et aucune regle ne manque pour le dire.
            case DeliveryEvent.PayloadOneofCase.NoDriverFound:
                await RembourserAsync(
                    dispatcher,
                    message.NoDriverFound.DeliveryId,
                    "DELIVERY_NO_DRIVER",

                    // AUCUN LIVREUR N'A ETE TROUVE : personne ne s'est deplace, donc
                    // rien n'est retenu et tout l'encaissement est du.
                    retenueXof: 0,
                    message.NoDriverFound.OccurredAt?.ToDateTimeOffset() ?? DateTimeOffset.UtcNow,
                    cancellationToken).ConfigureAwait(false);
                break;

            // ANNULEE OU ECHOUEE : ce qui est du depend de ce qui s'etait deja passe.
            //
            // CE TROU EXISTAIT DEJA POUR LES COLIS, et il n'a pas ete ouvert par le
            // transport de personnes : un expediteur dont la course etait annulee
            // lisait « HBA revient vers vous au sujet du montant preleve », et rien
            // ne garantissait que quelqu'un revienne. La file le garantit maintenant
            // pour les deux natures de mission.
            case DeliveryEvent.PayloadOneofCase.Cancelled:

                // DEDOMMAGER AVANT DE REMBOURSER, ET L'ORDRE N'EST PAS INDIFFERENT.
                // Les deux ecritures viennent du meme chiffre : si la seconde
                // echoue, la file de remboursement manque une ligne que la finance
                // verra manquer. Si c'etait la premiere, un livreur ne serait pas
                // paye et PERSONNE ne le verrait — un relevé n'a pas de trou visible.
                await DedommagerAsync(
                    dispatcher,
                    message.Cancelled.DeliveryId,
                    message.Cancelled.DriverId,
                    message.Cancelled.Reference,
                    message.Cancelled.CancellationFee?.Amount ?? 0,
                    message.Cancelled.OccurredAt?.ToDateTimeOffset() ?? DateTimeOffset.UtcNow,
                    cancellationToken).ConfigureAwait(false);

                await RembourserAsync(
                    dispatcher,
                    message.Cancelled.DeliveryId,
                    "DELIVERY_CANCELLED",
                    retenueXof: message.Cancelled.CancellationFee?.Amount ?? 0,
                    message.Cancelled.OccurredAt?.ToDateTimeOffset() ?? DateTimeOffset.UtcNow,
                    cancellationToken).ConfigureAwait(false);
                break;

            case DeliveryEvent.PayloadOneofCase.Failed:

                // MEME TRAITEMENT QU'UNE ANNULATION, ET C'EST LA DECISION DU POINT 34
                // (2 octobre 2026) : le livreur s'est deplace et a porte le colis.
                await DedommagerAsync(
                    dispatcher,
                    message.Failed.DeliveryId,
                    message.Failed.DriverId,
                    message.Failed.Reference,
                    message.Failed.CancellationFee?.Amount ?? 0,
                    message.Failed.OccurredAt?.ToDateTimeOffset() ?? DateTimeOffset.UtcNow,
                    cancellationToken).ConfigureAwait(false);

                await RembourserAsync(
                    dispatcher,
                    message.Failed.DeliveryId,
                    "DELIVERY_FAILED",
                    retenueXof: message.Failed.CancellationFee?.Amount ?? 0,
                    message.Failed.OccurredAt?.ToDateTimeOffset() ?? DateTimeOffset.UtcNow,
                    cancellationToken).ConfigureAwait(false);
                break;

            default:
                break;
        }
    }

    private static async Task CrediterAsync(
        IDispatcher dispatcher,
        DeliveryCompleted livree,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(livree.DeliveryId, out var deliveryId))
        {
            return;
        }

        await dispatcher
            .SendAsync(
                new RecordMissionEarningCommand(
                    ServiceType.Delivery,
                    deliveryId,
                    livree.DriverId,
                    livree.Reference,
                    livree.DriverEarning?.Amount ?? 0,
                    livree.OccurredAt?.ToDateTimeOffset() ?? DateTimeOffset.UtcNow),
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Inscrit un remboursement a faire, si l'argent est bien arrive.
    /// </summary>
    ///
    /// <remarks>
    /// LA COMMANDE VERIFIE ELLE-MEME QUE LE PAIEMENT A REUSSI : une livraison
    /// annulee avant paiement ne produit aucune ligne. C'est pourquoi on l'appelle
    /// sans condition — tester ici obligerait ce consommateur a connaitre l'etat des
    /// intentions, ce qui n'est pas son affaire.
    /// </remarks>
    private static async Task RembourserAsync(
        IDispatcher dispatcher,
        string deliveryId,
        string motif,
        long retenueXof,
        DateTimeOffset quand,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(deliveryId, out var id))
        {
            return;
        }

        await dispatcher
            .SendAsync(
                new OpenRefundDueCommand(ServiceType.Delivery, id, motif, retenueXof, quand),
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Credite le livreur de ce que lui laisse une mission qui n'a pas abouti.
    /// </summary>
    ///
    /// <remarks>
    /// APPELEE SANS CONDITION, COMME LE REMBOURSEMENT, et pour la meme raison : la
    /// commande sait seule si la retenue vaut quelque chose. Tester ici obligerait
    /// ce consommateur a connaitre la politique d'annulation, ce qui n'est pas son
    /// affaire.
    ///
    /// LE MONTANT VIENT DE L'EVENEMENT : c'est le domaine de Delivery qui a decide
    /// qu'il est nul quand aucun livreur n'etait affecte.
    /// </remarks>
    private static async Task DedommagerAsync(
        IDispatcher dispatcher,
        string deliveryId,
        string driverId,
        string reference,
        long retenueXof,
        DateTimeOffset quand,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(deliveryId, out var id))
        {
            return;
        }

        await dispatcher
            .SendAsync(
                new RecordMissionCompensationCommand(
                    ServiceType.Delivery,
                    id,
                    driverId,
                    reference,
                    retenueXof,
                    quand),
                cancellationToken)
            .ConfigureAwait(false);
    }
}
