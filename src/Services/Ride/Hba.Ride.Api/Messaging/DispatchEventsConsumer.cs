using Hba.BuildingBlocks.Application.Messaging;
using Hba.BuildingBlocks.Messaging.Kafka;
using Hba.Contracts.Common.V1;
using Hba.Contracts.Dispatch.V1;
using Hba.Ride.Application.Features.Rides.Commands;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Hba.Ride.Api.Messaging;

/// <summary>
/// Ce qui fait avancer une course au-delà de la demande : le moteur de dispatch.
/// </summary>
///
/// <remarks>
/// SANS CE CONSOMMATEUR, UNE COURSE RESTAIT À <c>REQUESTED</c> POUR TOUJOURS. Les
/// trois commandes internes de Ride existaient depuis la phase 3 et rien ne les
/// appelait : c'était le dernier maillon manquant du back.
///
/// IL FILTRE SUR LA NATURE, ET CE TEST N'EST PAS OPTIONNEL. Le topic porte aussi
/// les offres de colis : sans lui, l'acceptation d'une livraison arriverait ici
/// avec son identifiant de livraison, et <c>AssignRideDriverCommand</c> chercherait
/// une course qui n'existe pas — introuvable, trois réessais, message abandonné.
///
/// LES REFUS SONT JOURNALISÉS, PAS LEVÉS. Les commandes de Ride rendent un
/// <c>Result</c>, et un refus ici veut dire que la course a changé d'état pendant
/// que le message voyageait — annulée, déjà affectée. Lever ferait réessayer trois
/// fois un message dont la réponse ne changera pas, puis l'abandonner ; et
/// chacune de ces commandes traite déjà le rejeu comme un succès.
/// </remarks>
public sealed class DispatchEventsConsumer(
    IServiceScopeFactory scopeFactory,
    IOptions<KafkaOptions> options,
    ILogger<DispatchEventsConsumer> logger)
    : KafkaConsumerBase<DispatchEvent>(scopeFactory, options, logger)
{
    protected override string Topic => KafkaTopics.DispatchEvents;

    protected override Guid GetEventId(DispatchEvent message)
    {
        ArgumentNullException.ThrowIfNull(message);
        return Guid.TryParse(message.Envelope?.EventId, out var id) ? id : Guid.CreateVersion7();
    }

    protected override string GetEventType(DispatchEvent message)
    {
        ArgumentNullException.ThrowIfNull(message);
        return message.Envelope?.EventType ?? "unknown";
    }

    protected override async Task HandleAsync(
        DispatchEvent message,
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
            // UNE OFFRE PARTIE OUVRE LA RECHERCHE, N'IMPORTE QUELLE VAGUE.
            //
            // Une première vague sans candidat ne publie aucune offre : si seule la
            // vague 1 ouvrait la recherche, une course trouvée au deuxième rayon
            // resterait affichée « demandée » au passager pendant que des
            // chauffeurs la regardent déjà. La transition est idempotente — la
            // commande sort tout de suite si la recherche est déjà ouverte.
            case DispatchEvent.PayloadOneofCase.OfferSent:
                await EnvoyerAsync(
                    dispatcher,
                    message.OfferSent.ReferenceId,
                    id => new StartRideSearchCommand(
                        id,
                        message.OfferSent.ExpiresAt?.ToDateTimeOffset() ?? DateTimeOffset.UtcNow),
                    cancellationToken).ConfigureAwait(false);
                break;

            // Le gagnant est déjà départagé côté Dispatch : ici, on enregistre.
            case DispatchEvent.PayloadOneofCase.OfferAccepted:
                await EnvoyerAsync(
                    dispatcher,
                    message.OfferAccepted.ReferenceId,
                    id => new AssignRideDriverCommand(
                        id,
                        message.OfferAccepted.DriverId,
                        message.OfferAccepted.AcceptedAt?.ToDateTimeOffset() ?? DateTimeOffset.UtcNow),
                    cancellationToken).ConfigureAwait(false);
                break;

            case DispatchEvent.PayloadOneofCase.DispatchExhausted:
                await EnvoyerAsync(
                    dispatcher,
                    message.DispatchExhausted.ReferenceId,
                    id => new MarkNoRideDriverFoundCommand(
                        id,
                        message.DispatchExhausted.WavesAttempted,
                        message.DispatchExhausted.OccurredAt?.ToDateTimeOffset() ?? DateTimeOffset.UtcNow),
                    cancellationToken).ConfigureAwait(false);
                break;

            // UNE OFFRE ÉTEINTE NE CHANGE RIEN À LA COURSE. Elle libère le
            // chauffeur — c'est l'affaire de Driver — et le moteur enchaîne tout
            // seul sur la vague suivante. La course, elle, cherche toujours.
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
            logger.LogWarning("Identifiant de course illisible dans un événement de dispatch : {Reference}.", referenceId);
            return;
        }

        var issue = await dispatcher.SendAsync(commande(rideId), cancellationToken).ConfigureAwait(false);

        if (issue.Failed)
        {
            // LE MESSAGE EST ACQUITTÉ MALGRÉ LE REFUS, et c'est le bon choix : la
            // course a changé d'état pendant le voyage du message, et la réponse
            // ne changera pas au troisième essai. Le journal garde la trace.
            logger.LogInformation(
                "Événement de dispatch sans effet sur la course {RideId} : {Code} — {Message}",
                rideId,
                issue.Error.Code,
                issue.Error.Message);
        }
    }

    private static ServiceType Nature(DispatchEvent message) => message.PayloadCase switch
    {
        DispatchEvent.PayloadOneofCase.OfferSent => message.OfferSent.ServiceType,
        DispatchEvent.PayloadOneofCase.OfferAccepted => message.OfferAccepted.ServiceType,
        DispatchEvent.PayloadOneofCase.OfferExpired => message.OfferExpired.ServiceType,
        DispatchEvent.PayloadOneofCase.DispatchExhausted => message.DispatchExhausted.ServiceType,
        _ => ServiceType.Unspecified,
    };
}
