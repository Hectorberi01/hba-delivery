using System.Diagnostics;
using Google.Protobuf;
using Hba.BuildingBlocks.Messaging.Outbox;
using Microsoft.EntityFrameworkCore;

namespace Hba.BuildingBlocks.Persistence;

/// <summary>
/// Dépose un message dans l'Outbox du service. N'écrit rien de lui-même : c'est
/// le SaveChanges du DbContext qui valide, dans la même transaction que le
/// changement métier.
/// </summary>
public sealed class EfOutbox(DbContext context) : IOutbox
{
    public void Enqueue(string topic, string partitionKey, string eventType, IMessage payload)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(payload);

        context.Set<OutboxMessage>().Add(new OutboxMessage
        {
            Id = Guid.CreateVersion7(),
            Topic = topic,
            PartitionKey = partitionKey,
            EventType = eventType,
            Payload = payload.ToByteArray(),
            OccurredAt = DateTimeOffset.UtcNow,
            Attempts = 0,
            TraceId = Activity.Current?.TraceId.ToString(),
            CorrelationId = Activity.Current?.RootId,
        });
    }
}
