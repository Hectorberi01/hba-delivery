namespace Hba.BuildingBlocks.Persistence;

/// <summary>
/// Emplacement des tables techniques dans la base du service. Chaque service a
/// son schéma : identity, delivery, directory…
/// </summary>
public sealed class EfMessagingOptions
{
    public required string Schema { get; init; }

    public string OutboxTable { get; init; } = "outbox_messages";

    public string InboxTable { get; init; } = "inbox_messages";

    public string IdempotencyTable { get; init; } = "idempotency_records";

    public string QualifiedOutboxTable => $"{Schema}.{OutboxTable}";
}
