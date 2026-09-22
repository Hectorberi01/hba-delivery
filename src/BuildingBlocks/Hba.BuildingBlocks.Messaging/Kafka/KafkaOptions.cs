namespace Hba.BuildingBlocks.Messaging.Kafka;

public sealed class KafkaOptions
{
    public const string SectionName = "Kafka";

    public string BootstrapServers { get; set; } = "localhost:9092";

    /// <summary>Un groupe par service consommateur, ex. "delivery".</summary>
    public string ConsumerGroupId { get; set; } = string.Empty;

    /// <summary>Nom du service émetteur, reporté dans l'enveloppe.</summary>
    public string ProducerName { get; set; } = string.Empty;

    public string SecurityProtocol { get; set; } = "Plaintext";

    public string? SaslUsername { get; set; }

    public string? SaslPassword { get; set; }

    /// <summary>
    /// Producteur idempotent et acks=all : pas de doublon ni de perte côté broker.
    /// </summary>
    public bool EnableIdempotence { get; set; } = true;
}

/// <summary>Noms des topics. Un seul endroit où ils sont écrits.</summary>
public static class KafkaTopics
{
    public const string IdentityEvents = "hba.identity.events.v1";
    public const string DirectoryEvents = "hba.directory.events.v1";
    public const string DeliveryEvents = "hba.delivery.events.v1";
    public const string DispatchEvents = "hba.dispatch.events.v1";
    public const string DriverEvents = "hba.driver.events.v1";
    public const string PaymentEvents = "hba.payment.events.v1";
    public const string PricingEvents = "hba.pricing.events.v1";
    public const string NotificationCommands = "hba.notification.commands.v1";

    /// <summary>Publié par HBA Food quand une commande est prête à être collectée.</summary>
    public const string PlatformOrderEvents = "hba.platform.order.events.v1";
}
