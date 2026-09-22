using Google.Protobuf;

namespace Hba.BuildingBlocks.Messaging.Outbox;

/// <summary>
/// Ajoute un message à l'Outbox. N'écrit RIEN de lui-même : c'est
/// <c>IUnitOfWork.SaveChangesAsync</c> qui valide la transaction.
/// </summary>
public interface IOutbox
{
    void Enqueue(string topic, string partitionKey, string eventType, IMessage payload);
}

/// <summary>Lecture et marquage des messages, utilisés par le dispatcher.</summary>
public interface IOutboxStore
{
    Task<IReadOnlyList<OutboxMessage>> DequeueBatchAsync(int batchSize, CancellationToken cancellationToken);

    Task MarkPublishedAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);

    Task MarkFailedAsync(Guid id, string error, DateTimeOffset nextAttemptAt, CancellationToken cancellationToken);
}
