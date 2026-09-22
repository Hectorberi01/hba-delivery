namespace Hba.BuildingBlocks.Messaging.Outbox;

/// <summary>
/// Message en attente de publication. Écrit dans la MÊME transaction que le
/// changement métier : soit les deux, soit aucun des deux.
/// </summary>
public sealed class OutboxMessage
{
    public Guid Id { get; set; }

    /// <summary>Topic Kafka de destination, ex. hba.delivery.events.v1.</summary>
    public string Topic { get; set; } = string.Empty;

    /// <summary>
    /// Clé de partition. Toujours l'identifiant de l'agrégat, pour garantir
    /// l'ordre des événements d'une même livraison.
    /// </summary>
    public string PartitionKey { get; set; } = string.Empty;

    /// <summary>Nom qualifié du type, ex. hba.delivery.v1.DriverAssigned.</summary>
    public string EventType { get; set; } = string.Empty;

    /// <summary>Message protobuf sérialisé, enveloppe comprise.</summary>
    public byte[] Payload { get; set; } = [];

    public DateTimeOffset OccurredAt { get; set; }

    public DateTimeOffset? PublishedAt { get; set; }

    public int Attempts { get; set; }

    public string? LastError { get; set; }

    /// <summary>Prochaine tentative, pour le backoff exponentiel.</summary>
    public DateTimeOffset? NextAttemptAt { get; set; }

    public string? TraceId { get; set; }

    public string? CorrelationId { get; set; }
}
