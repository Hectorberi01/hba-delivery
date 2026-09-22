using Hba.BuildingBlocks.Messaging.Kafka;
using Hba.BuildingBlocks.Messaging.Outbox;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Hba.BuildingBlocks.Messaging.Extensions;

public static class MessagingServiceCollectionExtensions
{
    /// <summary>
    /// Producteur Kafka + dispatcher d'Outbox. Les implémentations d'IOutbox,
    /// IOutboxStore et IInboxStore restent à la charge de chaque service, parce
    /// qu'elles sont liées à son DbContext.
    /// </summary>
    public static IServiceCollection AddHbaMessaging(
        this IServiceCollection services,
        IConfiguration configuration,
        string producerName,
        string consumerGroupId)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<KafkaOptions>()
            .Bind(configuration.GetSection(KafkaOptions.SectionName))
            .Configure(o =>
            {
                o.ProducerName = producerName;
                o.ConsumerGroupId = consumerGroupId;
            })
            .ValidateOnStart();

        services.AddOptions<OutboxOptions>()
            .Bind(configuration.GetSection(OutboxOptions.SectionName))
            .ValidateOnStart();

        services.AddSingleton<IKafkaProducer, KafkaProducer>();
        services.AddHostedService<OutboxDispatcher>();

        return services;
    }
}
