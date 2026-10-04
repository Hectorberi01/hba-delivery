using Hba.BuildingBlocks.Application.Messaging;
using Hba.BuildingBlocks.Messaging.Kafka;
using Hba.Contracts.Ride.V1;
using Hba.Rating.Application.Features.Reviews;
using Hba.Rating.Domain.Reviews;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Hba.Rating.Api.Messaging;

/// <summary>
/// Une course achevee ouvre sa notation.
/// </summary>
///
/// <remarks>
/// SEULEMENT « ACHEVEE », JAMAIS « ANNULEE ». Une course annulee n'a rien a
/// noter : le passager n'a pas ete transporte, et le chauffeur n'a souvent rien
/// fait d'autre que se deplacer. Lui ouvrir une notation reviendrait a lui
/// offrir une etoile pour un service qui n'a pas eu lieu — dans les deux sens.
///
/// SANS CHAUFFEUR, RIEN NE S'OUVRE. Une course qui se termine sans que personne
/// n'ait ete affecte ne devrait pas exister ; si le cas arrive, il n'y a
/// personne a noter et le message est ignore sans bruit.
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

        if (message.PayloadCase != RideEvent.PayloadOneofCase.Completed)
        {
            return;
        }

        var fin = message.Completed;

        if (!Guid.TryParse(fin.RideId, out var rideId)
            || !Guid.TryParse(fin.DriverId, out var driverId)
            || string.IsNullOrWhiteSpace(fin.CustomerId))
        {
            return;
        }

        var dispatcher = services.GetRequiredService<IDispatcher>();

        await dispatcher
            .SendAsync(
                new OpenReviewCommand(
                    MissionType.Ride,
                    rideId,
                    fin.CustomerId,
                    driverId,
                    fin.OccurredAt?.ToDateTimeOffset() ?? DateTimeOffset.UtcNow),
                cancellationToken)
            .ConfigureAwait(false);
    }
}
