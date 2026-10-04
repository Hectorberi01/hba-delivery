using Hba.BuildingBlocks.Application.Messaging;
using Hba.BuildingBlocks.Messaging.Kafka;
using Hba.Contracts.Common.V1;
using Hba.Contracts.Payment.V1;
using Hba.Ride.Application.Features.Rides.Commands;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Hba.Ride.Api.Messaging;

/// <summary>
/// Ce qui met une course en route : l'encaissement.
/// </summary>
///
/// <remarks>
/// LE PRIX EST ENCAISSÉ AVANT LA RECHERCHE D'UN CHAUFFEUR (point 35), exactement
/// comme pour un colis. C'est donc ce consommateur, et lui seul, qui fait passer
/// une course de « demandée » à « recherche en cours » — et par conséquent qui
/// déclenche tout le dispatch.
///
/// SEUL LE WEBHOOK VÉRIFIÉ COMPTE, jamais le retour de l'application cliente. Un
/// passager qui revient sur l'écran « merci » n'a rien prouvé : il a fermé un
/// navigateur. Payment ne publie cet événement qu'après avoir relu la transaction
/// à la source.
///
/// IL FILTRE SUR LA NATURE, ET CE TEST N'EST PAS OPTIONNEL. Le topic porte aussi
/// les paiements de livraisons : sans lui, l'encaissement d'un colis arriverait
/// ici avec son identifiant de livraison et irait chercher une course qui n'existe
/// pas — introuvable, trois réessais, message abandonné.
/// </remarks>
public sealed class PaymentEventsConsumer(
    IServiceScopeFactory scopeFactory,
    IOptions<KafkaOptions> options,
    ILogger<PaymentEventsConsumer> logger)
    : KafkaConsumerBase<PaymentEvent>(scopeFactory, options, logger)
{
    protected override string Topic => KafkaTopics.PaymentEvents;

    protected override Guid GetEventId(PaymentEvent message)
    {
        ArgumentNullException.ThrowIfNull(message);
        return Guid.TryParse(message.Envelope?.EventId, out var id) ? id : Guid.CreateVersion7();
    }

    protected override string GetEventType(PaymentEvent message)
    {
        ArgumentNullException.ThrowIfNull(message);
        return message.Envelope?.EventType ?? "unknown";
    }

    protected override async Task HandleAsync(
        PaymentEvent message,
        IServiceProvider services,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(services);

        if (Nature(message) != ServiceType.Ride)
        {
            return;
        }

        var dispatcher = services.GetRequiredService<IDispatcher>();

        switch (message.PayloadCase)
        {
            // L'ARGENT EST ARRIVÉ : la recherche s'ouvre, et Dispatch l'apprend par
            // l'événement que cette transition publie.
            case PaymentEvent.PayloadOneofCase.Succeeded:
                await EnvoyerAsync(
                    dispatcher,
                    message.Succeeded.ReferenceId,
                    id => new StartRideSearchCommand(
                        id,
                        message.Succeeded.SucceededAt?.ToDateTimeOffset() ?? DateTimeOffset.UtcNow),
                    cancellationToken).ConfigureAwait(false);
                break;

            // LE PAIEMENT A ÉCHOUÉ : la course est close, et l'écran du passager
            // peut enfin le dire. Sans cette branche il regarderait un sablier qui
            // ne tournera jamais.
            case PaymentEvent.PayloadOneofCase.Failed:
                await EnvoyerAsync(
                    dispatcher,
                    message.Failed.ReferenceId,
                    id => new FailRidePaymentCommand(
                        id,
                        message.Failed.Reason,
                        message.Failed.OccurredAt?.ToDateTimeOffset() ?? DateTimeOffset.UtcNow),
                    cancellationToken).ConfigureAwait(false);
                break;

            // LE REMBOURSEMENT NE CHANGE PAS L'ÉTAT DE LA COURSE, et Ride n'a rien
            // à en faire aujourd'hui : elle reste annulée, ou sans chauffeur. C'est
            // la file des remboursements dûs, côté Payment, qui en tient le compte.
            // Le jour où l'écran du passager devra afficher « le montant vous a été
            // rendu », comme le fait celui des colis, c'est ici que ça s'ajoutera.
            default:
                break;
        }
    }

    private async Task EnvoyerAsync(
        IDispatcher dispatcher,
        string referenceId,
        Func<Guid, ICommand<Hba.Ride.Domain.Results.Result>> commande,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(referenceId, out var rideId))
        {
            logger.LogWarning(
                "Identifiant de course illisible dans un événement de paiement : {Reference}.",
                referenceId);
            return;
        }

        var issue = await dispatcher.SendAsync(commande(rideId), cancellationToken).ConfigureAwait(false);

        if (issue.Failed)
        {
            // LE MESSAGE EST ACQUITTÉ MALGRÉ LE REFUS : la course a changé d'état
            // pendant que le message voyageait — annulée par le passager, le plus
            // souvent — et la réponse ne changera pas au troisième essai.
            logger.LogInformation(
                "Événement de paiement sans effet sur la course {RideId} : {Code} — {Message}",
                rideId,
                issue.Error.Code,
                issue.Error.Message);
        }
    }

    private static ServiceType Nature(PaymentEvent message) => message.PayloadCase switch
    {
        PaymentEvent.PayloadOneofCase.Succeeded => message.Succeeded.ServiceType,
        PaymentEvent.PayloadOneofCase.Failed => message.Failed.ServiceType,
        PaymentEvent.PayloadOneofCase.Refunded => message.Refunded.ServiceType,
        _ => ServiceType.Unspecified,
    };
}
