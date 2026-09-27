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

    /// <summary>
    /// Journal des lectures de données personnelles. Posé dans TOUS les
    /// services parce que la forme est commune ; il ne se remplit que là où
    /// un handler le sollicite — aujourd'hui Directory et Delivery.
    /// </summary>
    public string PersonalDataReadTable { get; init; } = "personal_data_reads";

    public string QualifiedOutboxTable => $"{Schema}.{OutboxTable}";
}
