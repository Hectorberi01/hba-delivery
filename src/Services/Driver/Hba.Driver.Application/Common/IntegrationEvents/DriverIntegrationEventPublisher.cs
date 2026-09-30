using System.Diagnostics;
using Google.Protobuf.WellKnownTypes;
using Hba.BuildingBlocks.Domain;
using Hba.BuildingBlocks.Messaging.Kafka;
using Hba.BuildingBlocks.Messaging.Outbox;
using Hba.Contracts.Common.V1;
using Hba.Contracts.Driver.V1;
using DomainEvents = Hba.Driver.Domain.Drivers.Events;
using DomainOperational = Hba.Driver.Domain.Drivers.OperationalStatus;
using DomainVerification = Hba.Driver.Domain.Drivers.VerificationStatus;
using DriverEntity = Hba.Driver.Domain.Drivers.DriverAggregate;

namespace Hba.Driver.Application.Common.IntegrationEvents;

/// <summary>
/// Traduit les faits du domaine Driver en messages Kafka et les depose dans
/// l'Outbox. Tous les faits ne sortent pas : seuls ceux dont un autre service
/// a besoin.
/// </summary>
public interface IDriverIntegrationEventPublisher
{
    void Publish(IOutbox outbox, DriverEntity driver, IDomainEvent domainEvent);
}

/// L'Outbox est passee en parametre plutot qu'injectee : EfOutbox ecrit dans le
/// DbContext, et le DbContext a besoin de ce publieur. Les injecter tous les
/// deux formerait un cycle que le conteneur refuse de resoudre.
public sealed class DriverIntegrationEventPublisher : IDriverIntegrationEventPublisher
{
    private const string Producer = "driver";
    private const string AggregateType = "driver";

    public void Publish(IOutbox outbox, DriverEntity driver, IDomainEvent domainEvent)
    {
        ArgumentNullException.ThrowIfNull(outbox);
        ArgumentNullException.ThrowIfNull(driver);
        ArgumentNullException.ThrowIfNull(domainEvent);

        var message = Map(driver, domainEvent);
        if (message is null)
        {
            return;
        }

        outbox.Enqueue(
            KafkaTopics.DriverEvents,
            driver.Id.ToString(),
            message.Envelope.EventType,
            message);
    }

    private static DriverEvent? Map(DriverEntity driver, IDomainEvent domainEvent) => domainEvent switch
    {
        DomainEvents.DriverRegistered e => Wrap(driver, e, "hba.driver.v1.DriverRegistered", m => m.Registered = new DriverRegistered
        {
            DriverId = e.DriverId.ToString(),
            Phone = e.Phone,
            RegisteredAt = Timestamp.FromDateTimeOffset(e.OccurredAt),
        }),

        DomainEvents.DriverKycReviewed e => Wrap(driver, e, "hba.driver.v1.DriverKycReviewed", m => m.KycReviewed = new DriverKycReviewed
        {
            DriverId = e.DriverId.ToString(),
            Result = MapVerification(e.Result),
            Reason = e.Reason,
            ReviewedBy = e.ReviewedBy,
            ReviewedAt = Timestamp.FromDateTimeOffset(e.OccurredAt),
        }),

        // LE CHANGEMENT D'ETAT SORT, et c'est le dispatch qui en depend : un
        // livreur passe hors ligne ne doit plus figurer dans une vague.
        DomainEvents.DriverStatusChanged e => Wrap(driver, e, "hba.driver.v1.DriverStatusChanged", m => m.StatusChanged = new DriverStatusChanged
        {
            DriverId = e.DriverId.ToString(),
            Previous = MapOperational(e.Previous),
            Current = MapOperational(e.Current),
            OccurredAt = Timestamp.FromDateTimeOffset(e.OccurredAt),
        }),

        _ => null,
    };

    private static DriverEvent Wrap(
        DriverEntity driver,
        IDomainEvent domainEvent,
        string eventType,
        Action<DriverEvent> fill)
    {
        var message = new DriverEvent
        {
            Envelope = new EventEnvelope
            {
                EventId = domainEvent.EventId.ToString(),
                EventType = eventType,
                SchemaVersion = 1,
                AggregateType = AggregateType,
                AggregateId = driver.Id.ToString(),
                OccurredAt = Timestamp.FromDateTimeOffset(domainEvent.OccurredAt),
                TraceId = Activity.Current?.TraceId.ToString() ?? string.Empty,
                CorrelationId = Activity.Current?.RootId ?? string.Empty,
                ActorType = MapActor(domainEvent.Actor.Kind),
                ActorId = domainEvent.Actor.Id,
                Producer = Producer,
            },
        };

        fill(message);
        return message;
    }

    private static VerificationStatus MapVerification(DomainVerification status) => status switch
    {
        DomainVerification.PendingVerification => VerificationStatus.PendingVerification,
        DomainVerification.Verified => VerificationStatus.Verified,
        DomainVerification.Rejected => VerificationStatus.Rejected,
        DomainVerification.Suspended => VerificationStatus.Suspended,
        _ => VerificationStatus.Unspecified,
    };

    private static OperationalStatus MapOperational(DomainOperational status) => status switch
    {
        DomainOperational.Offline => OperationalStatus.Offline,
        DomainOperational.Available => OperationalStatus.Available,
        DomainOperational.Reserved => OperationalStatus.Reserved,
        DomainOperational.OnMission => OperationalStatus.OnMission,
        _ => OperationalStatus.Unspecified,
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
        ActorKind.Service => ActorType.Service,
        _ => ActorType.Unspecified,
    };
}
