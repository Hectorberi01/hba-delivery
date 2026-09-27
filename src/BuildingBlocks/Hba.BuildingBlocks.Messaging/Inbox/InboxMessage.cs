namespace Hba.BuildingBlocks.Messaging.Inbox;

/// <summary>
/// Trace d'un message déjà traité. Kafka livrant « au moins une fois », c'est
/// cette table qui rend les consommateurs idempotents.
/// </summary>
public sealed class InboxMessage
{
    /// <summary>EventId de l'enveloppe. Clé primaire : la déduplication est native.</summary>
    public Guid EventId { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string Topic { get; set; } = string.Empty;
    public DateTimeOffset ReceivedAt { get; set; }
    public DateTimeOffset? ProcessedAt { get; set; }
    public int Attempts { get; set; }
    public string? LastError { get; set; }
}

public interface IInboxStore
{
    Task<bool> TryBeginAsync(Guid eventId, string eventType, string topic, CancellationToken cancellationToken);

    Task MarkProcessedAsync(Guid eventId, CancellationToken cancellationToken);

    Task MarkFailedAsync(Guid eventId, string error, CancellationToken cancellationToken);
}
