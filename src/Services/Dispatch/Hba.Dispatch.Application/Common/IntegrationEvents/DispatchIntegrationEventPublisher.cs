using System.Diagnostics;
using Google.Protobuf.WellKnownTypes;
using Hba.BuildingBlocks.Domain;
using Hba.BuildingBlocks.Messaging.Kafka;
using Hba.BuildingBlocks.Messaging.Outbox;
using Hba.Contracts.Common.V1;
using Hba.Contracts.Dispatch.V1;
using DomainEvents = Hba.Dispatch.Domain.Dispatching.Events;
using DomainDispatch = Hba.Dispatch.Domain.Dispatching.DispatchAggregate;
using DomainServiceType = Hba.Dispatch.Domain.Dispatching.ServiceType;
using ProtoServiceType = Hba.Contracts.Common.V1.ServiceType;

namespace Hba.Dispatch.Application.Common.IntegrationEvents;

public interface IDispatchIntegrationEventPublisher
{
    void Publish(IOutbox outbox, DomainDispatch dispatch, IDomainEvent domainEvent);
}

/// L'Outbox est passee en parametre plutot qu'injectee : EfOutbox ecrit dans le
/// DbContext, et le DbContext a besoin de ce publieur.
public sealed class DispatchIntegrationEventPublisher : IDispatchIntegrationEventPublisher
{
    private const string Producer = "dispatch";
    private const string AggregateType = "dispatch";

    public void Publish(IOutbox outbox, DomainDispatch dispatch, IDomainEvent domainEvent)
    {
        ArgumentNullException.ThrowIfNull(outbox);
        ArgumentNullException.ThrowIfNull(dispatch);
        ArgumentNullException.ThrowIfNull(domainEvent);

        var message = Map(dispatch, domainEvent);
        if (message is null)
        {
            return;
        }

        // CLE DE PARTITION : LA MISSION, comme le fixe le contrat. C'est ce qui
        // garantit a Delivery, a Ride et a Driver de lire « offre envoyee » avant
        // « offre acceptee » pour une meme mission.
        //
        // L'IDENTIFIANT SEUL SUFFIT COMME CLE, SANS LA NATURE. Deux missions de
        // natures differentes au meme identifiant tomberaient dans la meme
        // partition, ce qui ordonne simplement leurs messages entre eux : c'est
        // inutile, jamais faux. Prefixer par la nature changerait la partition de
        // toutes les livraisons existantes et casserait l'ordre en cours de route.
        outbox.Enqueue(
            KafkaTopics.DispatchEvents,
            dispatch.ReferenceId.ToString(),
            message.Envelope.EventType,
            message);
    }

    private static DispatchEvent? Map(DomainDispatch dispatch, IDomainEvent domainEvent) => domainEvent switch
    {
        DomainEvents.OfferSent e => Wrap(dispatch, e, "hba.dispatch.v1.OfferSent", m => m.OfferSent = new OfferSent
        {
            OfferId = e.OfferId.ToString(),
            ReferenceId = e.ReferenceId.ToString(),
            ServiceType = Nature(e.ServiceType),
            DriverId = e.DriverId,
            WaveNumber = e.WaveNumber,
            ExpiresAt = Timestamp.FromDateTimeOffset(e.ExpiresAt),
        }),

        DomainEvents.OfferAccepted e => Wrap(dispatch, e, "hba.dispatch.v1.OfferAccepted", m => m.OfferAccepted = new OfferAccepted
        {
            OfferId = e.OfferId.ToString(),
            ReferenceId = e.ReferenceId.ToString(),
            ServiceType = Nature(e.ServiceType),
            DriverId = e.DriverId,
            AcceptedAt = Timestamp.FromDateTimeOffset(e.OccurredAt),
        }),

        DomainEvents.OfferExpired e => Wrap(dispatch, e, "hba.dispatch.v1.OfferExpired", m => m.OfferExpired = new OfferExpired
        {
            OfferId = e.OfferId.ToString(),
            ReferenceId = e.ReferenceId.ToString(),
            ServiceType = Nature(e.ServiceType),
            DriverId = e.DriverId,
        }),

        // SUPERSEDED ET DECLINED EMPRUNTENT « OfferExpired », faute de mieux
        // dans le contrat — et ce n'est pas un detail cosmetique : pour Driver,
        // les trois veulent dire la meme chose, « libere ce livreur ». Ajouter
        // deux messages au .proto serait plus juste, mais toucher un contrat
        // partage pour une nuance que personne ne lit encore attendra un
        // besoin reel.
        DomainEvents.OfferSuperseded e => Wrap(dispatch, e, "hba.dispatch.v1.OfferExpired", m => m.OfferExpired = new OfferExpired
        {
            OfferId = e.OfferId.ToString(),
            ReferenceId = e.ReferenceId.ToString(),
            ServiceType = Nature(e.ServiceType),
            DriverId = e.DriverId,
        }),

        DomainEvents.OfferDeclined e => Wrap(dispatch, e, "hba.dispatch.v1.OfferExpired", m => m.OfferExpired = new OfferExpired
        {
            OfferId = e.OfferId.ToString(),
            ReferenceId = e.ReferenceId.ToString(),
            ServiceType = Nature(e.ServiceType),
            DriverId = e.DriverId,
        }),

        DomainEvents.DispatchExhausted e => Wrap(dispatch, e, "hba.dispatch.v1.DispatchExhausted", m => m.DispatchExhausted = new DispatchExhausted
        {
            ReferenceId = e.ReferenceId.ToString(),
            ServiceType = Nature(e.ServiceType),
            WavesAttempted = e.WavesAttempted,
            OccurredAt = Timestamp.FromDateTimeOffset(e.OccurredAt),
        }),

        _ => null,
    };

    private static DispatchEvent Wrap(
        DomainDispatch dispatch,
        IDomainEvent domainEvent,
        string eventType,
        Action<DispatchEvent> fill)
    {
        var message = new DispatchEvent
        {
            Envelope = new EventEnvelope
            {
                EventId = domainEvent.EventId.ToString(),
                EventType = eventType,
                SchemaVersion = 1,
                AggregateType = AggregateType,
                AggregateId = dispatch.Id.ToString(),
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

    /// <summary>
    /// La nature de la mission, traduite pour le contrat.
    /// </summary>
    ///
    /// <remarks>
    /// UNE NATURE NON PRECISEE NE PEUT PAS SORTIR : l'agregat la refuse a la
    /// creation. La branche par defaut existe pour que le compilateur soit content
    /// et pour que, si jamais une ligne ancienne mal reprise passait, le message
    /// porte UNSPECIFIED — ce qu'aucun consommateur ne traite — plutot qu'une
    /// nature inventee qui ferait agir le mauvais service.
    /// </remarks>
    private static ProtoServiceType Nature(DomainServiceType nature) => nature switch
    {
        DomainServiceType.Delivery => ProtoServiceType.Delivery,
        DomainServiceType.Ride => ProtoServiceType.Ride,
        _ => ProtoServiceType.Unspecified,
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
