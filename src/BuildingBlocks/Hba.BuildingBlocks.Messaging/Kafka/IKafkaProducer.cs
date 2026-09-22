using Hba.BuildingBlocks.Messaging.Outbox;

namespace Hba.BuildingBlocks.Messaging.Kafka;

public interface IKafkaProducer
{
    Task ProduceAsync(OutboxMessage message, CancellationToken cancellationToken);
}
