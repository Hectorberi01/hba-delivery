using System.Text;
using Confluent.Kafka;
using Hba.BuildingBlocks.Messaging.Outbox;
using Microsoft.Extensions.Options;

namespace Hba.BuildingBlocks.Messaging.Kafka;

internal sealed class KafkaProducer : IKafkaProducer, IDisposable
{
    private readonly IProducer<string, byte[]> _producer;

    public KafkaProducer(IOptions<KafkaOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var value = options.Value;

        var config = new ProducerConfig
        {
            BootstrapServers = value.BootstrapServers,
            EnableIdempotence = value.EnableIdempotence,
            Acks = Acks.All,
            MessageSendMaxRetries = 5,
            CompressionType = CompressionType.Snappy,
            SecurityProtocol = Enum.Parse<SecurityProtocol>(value.SecurityProtocol, ignoreCase: true),
        };

        if (!string.IsNullOrWhiteSpace(value.SaslUsername))
        {
            config.SaslMechanism = SaslMechanism.ScramSha512;
            config.SaslUsername = value.SaslUsername;
            config.SaslPassword = value.SaslPassword;
        }

        _producer = new ProducerBuilder<string, byte[]>(config).Build();
    }

    public async Task ProduceAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        var kafkaMessage = new Message<string, byte[]>
        {
            Key = message.PartitionKey,
            Value = message.Payload,
            Headers =
            [
                new Header("hba-event-type", Encoding.UTF8.GetBytes(message.EventType)),
                new Header("hba-event-id", Encoding.UTF8.GetBytes(message.Id.ToString())),
                new Header("traceparent", Encoding.UTF8.GetBytes(message.TraceId ?? string.Empty)),
                new Header("hba-correlation-id", Encoding.UTF8.GetBytes(message.CorrelationId ?? string.Empty)),
            ],
        };

        await _producer.ProduceAsync(message.Topic, kafkaMessage, cancellationToken).ConfigureAwait(false);
    }

    public void Dispose()
    {
        _producer.Flush(TimeSpan.FromSeconds(5));
        _producer.Dispose();
    }
}
