using Hba.BuildingBlocks.Application.Messaging;
using Hba.BuildingBlocks.Messaging.Kafka;
using Hba.Contracts.Dispatch.V1;
using Hba.Driver.Application.Features.Drivers.Commands;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Hba.Driver.Api.Messaging;

/// <summary>
/// L'etat operationnel du livreur suit les offres : reserve quand une offre
/// part vers lui, libere quand elle s'eteint, en mission quand il gagne.
/// </summary>
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

        var (driverId, outcome) = message.PayloadCase switch
        {
            DispatchEvent.PayloadOneofCase.OfferSent =>
                (message.OfferSent.DriverId, (DispatchOutcome?)DispatchOutcome.Reserved),
            DispatchEvent.PayloadOneofCase.OfferExpired =>
                (message.OfferExpired.DriverId, DispatchOutcome.Released),
            DispatchEvent.PayloadOneofCase.OfferAccepted =>
                (message.OfferAccepted.DriverId, DispatchOutcome.OnMission),
            _ => (string.Empty, null),
        };

        if (outcome is null || !Guid.TryParse(driverId, out var id))
        {
            return;
        }

        var dispatcher = services.GetRequiredService<IDispatcher>();

        await dispatcher
            .SendAsync(new ApplyDispatchOutcomeCommand(id, outcome.Value), cancellationToken)
            .ConfigureAwait(false);
    }
}
