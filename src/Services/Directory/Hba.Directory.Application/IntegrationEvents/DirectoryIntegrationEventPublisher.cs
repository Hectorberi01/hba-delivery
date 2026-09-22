using Google.Protobuf.WellKnownTypes;
using Hba.BuildingBlocks.Domain;
using Hba.BuildingBlocks.Messaging.Kafka;
using Hba.BuildingBlocks.Messaging.Outbox;
using Hba.Contracts.Common.V1;
using Hba.Contracts.Directory.V1;
// Customer, Merchant et les noms d'événements existent des deux côtés : comme
// messages protobuf et comme types du domaine. Les alias tranchent.
using CustomerAggregate = Hba.Directory.Domain.Customers.Customer;
using CustomerEvents = Hba.Directory.Domain.Customers.Events;
using MerchantAggregate = Hba.Directory.Domain.Merchants.Merchant;
using MerchantEvents = Hba.Directory.Domain.Merchants.Events;

namespace Hba.Directory.Application.IntegrationEvents;

public interface IDirectoryIntegrationEventPublisher
{
    void Publish(IOutbox outbox, CustomerAggregate customer, IDomainEvent domainEvent);

    void Publish(IOutbox outbox, MerchantAggregate merchant, IDomainEvent domainEvent);
}

/// <summary>
/// Tout ne sort pas : un changement de nom d'un client ne regarde personne.
/// Ce qui sort, c'est ce dont un autre service a besoin — la création d'un
/// profil, l'existence d'un commerçant, et surtout le déplacement ou la
/// fermeture d'un point de collecte, que Dispatch doit connaître.
/// </summary>
/// L'Outbox est passée en paramètre plutôt qu'injectée : EfOutbox écrit dans le
/// DbContext, et le DbContext a besoin de ce publieur. Les injecter tous les
/// deux formerait un cycle que le conteneur refuse de résoudre — y compris au
/// moment où dotnet ef instancie le contexte.
public sealed class DirectoryIntegrationEventPublisher : IDirectoryIntegrationEventPublisher
{
    private const string Producer = "directory";

    public void Publish(IOutbox outbox, CustomerAggregate customer, IDomainEvent domainEvent)
    {
        ArgumentNullException.ThrowIfNull(outbox);
        ArgumentNullException.ThrowIfNull(customer);
        ArgumentNullException.ThrowIfNull(domainEvent);

        if (domainEvent is not CustomerEvents.CustomerProfileCreated created)
        {
            return;
        }

        var message = new DirectoryEvent
        {
            Envelope = Envelope(
                created.EventId,
                "hba.directory.v1.CustomerProfileCreated",
                "customer",
                customer.Id.ToString(),
                created.OccurredAt,
                created.Actor),
            CustomerCreated = new CustomerProfileCreated
            {
                CustomerId = customer.Id.ToString(),
                Phone = created.Phone,
                OccurredAt = Timestamp.FromDateTimeOffset(created.OccurredAt),
            },
        };

        Enqueue(outbox, customer.Id.ToString(), message);
    }

    public void Publish(IOutbox outbox, MerchantAggregate merchant, IDomainEvent domainEvent)
    {
        ArgumentNullException.ThrowIfNull(outbox);
        ArgumentNullException.ThrowIfNull(merchant);
        ArgumentNullException.ThrowIfNull(domainEvent);

        DirectoryEvent? message = domainEvent switch
        {
            MerchantEvents.MerchantCreated e => new DirectoryEvent
            {
                Envelope = Envelope(e.EventId, "hba.directory.v1.MerchantCreated", "merchant", merchant.Id.ToString(), e.OccurredAt, e.Actor),
                MerchantCreated = new MerchantCreated
                {
                    MerchantId = merchant.Id.ToString(),
                    LegalName = e.LegalName,
                    OccurredAt = Timestamp.FromDateTimeOffset(e.OccurredAt),
                },
            },

            MerchantEvents.PickupPointChanged e => new DirectoryEvent
            {
                Envelope = Envelope(e.EventId, "hba.directory.v1.PickupPointChanged", "merchant", merchant.Id.ToString(), e.OccurredAt, e.Actor),
                PickupPointChanged = new PickupPointChanged
                {
                    MerchantId = merchant.Id.ToString(),
                    PickupPointId = e.PickupPointId.ToString(),
                    Location = ToLocation(e.Address),
                    IsActive = e.IsActive,
                    OccurredAt = Timestamp.FromDateTimeOffset(e.OccurredAt),
                },
            },

            MerchantEvents.MerchantDeactivated e => new DirectoryEvent
            {
                Envelope = Envelope(e.EventId, "hba.directory.v1.MerchantDeactivated", "merchant", merchant.Id.ToString(), e.OccurredAt, e.Actor),
                MerchantDeactivated = new MerchantDeactivated
                {
                    MerchantId = merchant.Id.ToString(),
                    Reason = e.Reason,
                    OccurredAt = Timestamp.FromDateTimeOffset(e.OccurredAt),
                },
            },

            _ => null,
        };

        if (message is not null)
        {
            Enqueue(outbox, merchant.Id.ToString(), message);
        }
    }

    private static void Enqueue(IOutbox outbox, string partitionKey, DirectoryEvent message)
        => outbox.Enqueue(KafkaTopics.DirectoryEvents, partitionKey, message.Envelope.EventType, message);

    private static Contracts.Common.V1.Location ToLocation(Domain.ValueObjects.Address address) => new()
    {
        Point = new GeoPoint { Latitude = address.Point.Latitude, Longitude = address.Point.Longitude },
        Landmark = address.Landmark,
        Phone = address.Phone,
        ContactName = address.ContactName,
        Notes = address.Notes ?? string.Empty,
    };

    private static EventEnvelope Envelope(
        Guid eventId,
        string eventType,
        string aggregateType,
        string aggregateId,
        DateTimeOffset occurredAt,
        Actor actor)
        => new()
        {
            EventId = eventId.ToString(),
            EventType = eventType,
            SchemaVersion = 1,
            AggregateType = aggregateType,
            AggregateId = aggregateId,
            OccurredAt = Timestamp.FromDateTimeOffset(occurredAt),
            TraceId = System.Diagnostics.Activity.Current?.TraceId.ToString() ?? string.Empty,
            CorrelationId = System.Diagnostics.Activity.Current?.RootId ?? string.Empty,
            ActorType = MapActor(actor.Kind),
            ActorId = actor.Id,
            Producer = Producer,
        };

    private static ActorType MapActor(ActorKind kind) => kind switch
    {
        ActorKind.Customer => ActorType.Customer,
        ActorKind.Driver => ActorType.Driver,
        ActorKind.Merchant => ActorType.Merchant,
        ActorKind.Partner => ActorType.Partner,
        ActorKind.Admin => ActorType.Admin,
        ActorKind.Dispatch => ActorType.Dispatch,
        ActorKind.PaymentProvider => ActorType.PaymentProvider,
        ActorKind.Scheduler => ActorType.Scheduler,
        _ => ActorType.Unspecified,
    };
}
