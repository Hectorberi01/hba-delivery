using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Hba.BuildingBlocks.Domain;
using Hba.BuildingBlocks.Messaging.Kafka;
using Hba.BuildingBlocks.Messaging.Outbox;
using Hba.Contracts.Common.V1;
using Hba.Contracts.Delivery.V1;
using Hba.Delivery.Domain.Deliveries;
using Hba.Delivery.Domain.Deliveries.Events;
using DomainEvents = Hba.Delivery.Domain.Deliveries.Events;

namespace Hba.Delivery.Application.IntegrationEvents;

/// <summary>
/// Traduit les faits de domaine en messages Kafka et les dépose dans l'Outbox.
/// Tous les événements de domaine ne sortent pas : seuls ceux dont un autre
/// service a besoin.
/// </summary>
public interface IDeliveryIntegrationEventPublisher
{
    void Publish(IOutbox outbox, DeliveryAggregate delivery, IDomainEvent domainEvent);
}

/// L'Outbox est passée en paramètre plutôt qu'injectée : EfOutbox écrit dans le
/// DbContext, et le DbContext a besoin de ce publieur. Les injecter tous les
/// deux formerait un cycle que le conteneur refuse de résoudre — y compris au
/// moment où dotnet ef instancie le contexte.
public sealed class DeliveryIntegrationEventPublisher : IDeliveryIntegrationEventPublisher
{
    private const string Producer = "delivery";
    private const string AggregateType = "delivery";

    public void Publish(IOutbox outbox, DeliveryAggregate delivery, IDomainEvent domainEvent)
    {
        ArgumentNullException.ThrowIfNull(delivery);
        ArgumentNullException.ThrowIfNull(domainEvent);

        ArgumentNullException.ThrowIfNull(outbox);

        var message = Map(delivery, domainEvent);
        if (message is null)
        {
            return;
        }

        outbox.Enqueue(
            KafkaTopics.DeliveryEvents,
            delivery.Id.ToString(),
            message.Envelope.EventType,
            message);
    }

    private static DeliveryEvent? Map(DeliveryAggregate delivery, IDomainEvent domainEvent) => domainEvent switch
    {
        DomainEvents.DeliveryCreated e => Wrap(delivery, e, "hba.delivery.v1.DeliveryCreated", m => m.Created = new Contracts.Delivery.V1.DeliveryCreated
        {
            DeliveryId = delivery.Id.ToString(),
            Reference = delivery.Reference,
            Source = MapSource(delivery.Source),
            CustomerId = delivery.CustomerId ?? string.Empty,
            MerchantId = delivery.MerchantId ?? string.Empty,
            Pickup = MapLocation(delivery.Pickup),
            Dropoff = MapLocation(delivery.Dropoff),
            Total = MapMoney(e.Total.Amount),
        }),

        DomainEvents.DeliveryConfirmed e => Wrap(delivery, e, "hba.delivery.v1.DeliveryConfirmed", m => m.Confirmed = new Contracts.Delivery.V1.DeliveryConfirmed
        {
            DeliveryId = delivery.Id.ToString(),
            Pickup = MapLocation(delivery.Pickup),
            Dropoff = MapLocation(delivery.Dropoff),
            DriverEarning = MapMoney(delivery.Pricing.DriverEarning.Amount),
            DistanceMeters = delivery.Pricing.DistanceMeters,
            PaidAt = Timestamp.FromDateTimeOffset(e.OccurredAt),
        }),

        DomainEvents.DriverAssigned e => Wrap(delivery, e, "hba.delivery.v1.DriverAssigned", m => m.DriverAssigned = new Contracts.Delivery.V1.DriverAssigned
        {
            DeliveryId = delivery.Id.ToString(),
            DriverId = e.DriverId,
            OfferId = e.OfferId,
            AssignedAt = Timestamp.FromDateTimeOffset(e.OccurredAt),
        }),

        DomainEvents.DriverArrivedAtPickup e => Wrap(delivery, e, "hba.delivery.v1.DriverArrivedAtPickup", m => m.DriverArrivedAtPickup = new Contracts.Delivery.V1.DriverArrivedAtPickup
        {
            DeliveryId = delivery.Id.ToString(),
            DriverId = e.DriverId,
            OccurredAt = Timestamp.FromDateTimeOffset(e.OccurredAt),
        }),

        DomainEvents.DeliveryPickedUp e => Wrap(delivery, e, "hba.delivery.v1.DeliveryPickedUp", m => m.PickedUp = new Contracts.Delivery.V1.DeliveryPickedUp
        {
            DeliveryId = delivery.Id.ToString(),
            DriverId = e.DriverId,
            OccurredAt = Timestamp.FromDateTimeOffset(e.OccurredAt),
        }),

        DomainEvents.DeliveryCompleted e => Wrap(delivery, e, "hba.delivery.v1.DeliveryCompleted", m => m.Delivered = new Contracts.Delivery.V1.DeliveryCompleted
        {
            DeliveryId = delivery.Id.ToString(),
            DriverId = e.DriverId,
            DriverEarning = MapMoney(e.DriverEarning.Amount),
            OccurredAt = Timestamp.FromDateTimeOffset(e.OccurredAt),
        }),

        DomainEvents.DeliveryCancelled e => Wrap(delivery, e, "hba.delivery.v1.DeliveryCancelled", m => m.Cancelled = new Contracts.Delivery.V1.DeliveryCancelled
        {
            DeliveryId = delivery.Id.ToString(),
            CancelledBy = MapActor(e.Actor.Kind),
            ActorId = e.Actor.Id,
            Reason = e.Reason,
            PreviousStatus = MapStatus(e.PreviousStatus),
            OccurredAt = Timestamp.FromDateTimeOffset(e.OccurredAt),
        }),

        DomainEvents.DeliveryFailed e => Wrap(delivery, e, "hba.delivery.v1.DeliveryFailed", m => m.Failed = new Contracts.Delivery.V1.DeliveryFailed
        {
            DeliveryId = delivery.Id.ToString(),
            Reason = e.Reason,
            OccurredAt = Timestamp.FromDateTimeOffset(e.OccurredAt),
        }),

        DomainEvents.NoDriverFound e => Wrap(delivery, e, "hba.delivery.v1.NoDriverFound", m => m.NoDriverFound = new Contracts.Delivery.V1.NoDriverFound
        {
            DeliveryId = delivery.Id.ToString(),
            WavesAttempted = e.WavesAttempted,
            OccurredAt = Timestamp.FromDateTimeOffset(e.OccurredAt),
        }),

        // Les autres faits (recherche ouverte, OTP refusé, désaffectation) restent
        // internes : aucun autre service n'en dépend aujourd'hui.
        _ => null,
    };

    private static DeliveryEvent Wrap(
        DeliveryAggregate delivery,
        IDomainEvent domainEvent,
        string eventType,
        Action<DeliveryEvent> fill)
    {
        var message = new DeliveryEvent
        {
            Envelope = new EventEnvelope
            {
                EventId = domainEvent.EventId.ToString(),
                EventType = eventType,
                SchemaVersion = 1,
                AggregateType = AggregateType,
                AggregateId = delivery.Id.ToString(),
                OccurredAt = Timestamp.FromDateTimeOffset(domainEvent.OccurredAt),
                TraceId = System.Diagnostics.Activity.Current?.TraceId.ToString() ?? string.Empty,
                CorrelationId = System.Diagnostics.Activity.Current?.RootId ?? string.Empty,
                PartnerId = delivery.PartnerId,
                ActorType = MapActor(domainEvent.Actor.Kind),
                ActorId = domainEvent.Actor.Id,
                Producer = Producer,
            },
        };

        fill(message);
        return message;
    }

    private static Money MapMoney(long amount) => new() { Amount = amount, Currency = "XOF" };

    private static Contracts.Common.V1.Location MapLocation(Domain.ValueObjects.Location location) => new()
    {
        Point = new GeoPoint { Latitude = location.Point.Latitude, Longitude = location.Point.Longitude },
        Landmark = location.Landmark,
        Phone = location.Phone,
        ContactName = location.ContactName,
        Notes = location.Notes ?? string.Empty,
    };

    private static Source MapSource(DeliverySource source) => source switch
    {
        DeliverySource.ClientApp => Source.ClientApp,
        DeliverySource.HbaExpress => Source.HbaExpress,
        DeliverySource.HbaFood => Source.HbaFood,
        DeliverySource.PartnerApi => Source.PartnerApi,
        DeliverySource.MerchantPortal => Source.ClientApp,
        _ => Source.Unspecified,
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

    private static Contracts.Delivery.V1.DeliveryStatus MapStatus(Domain.Deliveries.DeliveryStatus status) => status switch
    {
        Domain.Deliveries.DeliveryStatus.PendingPayment => Contracts.Delivery.V1.DeliveryStatus.PendingPayment,
        Domain.Deliveries.DeliveryStatus.PaymentFailed => Contracts.Delivery.V1.DeliveryStatus.PaymentFailed,
        Domain.Deliveries.DeliveryStatus.Paid => Contracts.Delivery.V1.DeliveryStatus.Paid,
        Domain.Deliveries.DeliveryStatus.SearchingDriver => Contracts.Delivery.V1.DeliveryStatus.SearchingDriver,
        Domain.Deliveries.DeliveryStatus.NoDriverFound => Contracts.Delivery.V1.DeliveryStatus.NoDriverFound,
        Domain.Deliveries.DeliveryStatus.DriverAssigned => Contracts.Delivery.V1.DeliveryStatus.DriverAssigned,
        Domain.Deliveries.DeliveryStatus.DriverAtPickup => Contracts.Delivery.V1.DeliveryStatus.DriverAtPickup,
        Domain.Deliveries.DeliveryStatus.PickedUp => Contracts.Delivery.V1.DeliveryStatus.PickedUp,
        Domain.Deliveries.DeliveryStatus.Delivered => Contracts.Delivery.V1.DeliveryStatus.Delivered,
        Domain.Deliveries.DeliveryStatus.Cancelled => Contracts.Delivery.V1.DeliveryStatus.Cancelled,
        Domain.Deliveries.DeliveryStatus.Failed => Contracts.Delivery.V1.DeliveryStatus.Failed,
        _ => Contracts.Delivery.V1.DeliveryStatus.Unspecified,
    };
}
