using System.Diagnostics;
using Google.Protobuf.WellKnownTypes;
using Hba.BuildingBlocks.Domain;
using Hba.BuildingBlocks.Messaging.Kafka;
using Hba.BuildingBlocks.Messaging.Outbox;
using Hba.Contracts.Common.V1;
using Hba.Contracts.Payment.V1;
using DomainEvents = Hba.Payment.Domain.Payments.Events;
using DomainIntent = Hba.Payment.Domain.Payments.PaymentIntent;

namespace Hba.Payment.Application.Common.IntegrationEvents;

/// <summary>
/// Traduit les faits du domaine Payment en messages Kafka et les depose dans
/// l'Outbox. Tous les faits ne sortent pas : seuls ceux dont un autre service a
/// besoin.
/// </summary>
public interface IPaymentIntegrationEventPublisher
{
    void Publish(IOutbox outbox, DomainIntent intent, IDomainEvent domainEvent);
}

/// L'Outbox est passee en parametre plutot qu'injectee : EfOutbox ecrit dans le
/// DbContext, et le DbContext a besoin de ce publieur. Les injecter tous les
/// deux formerait un cycle que le conteneur refuse de resoudre — y compris au
/// moment ou dotnet ef instancie le contexte.
public sealed class PaymentIntegrationEventPublisher : IPaymentIntegrationEventPublisher
{
    private const string Producer = "payment";
    private const string AggregateType = "payment_intent";

    public void Publish(IOutbox outbox, DomainIntent intent, IDomainEvent domainEvent)
    {
        ArgumentNullException.ThrowIfNull(outbox);
        ArgumentNullException.ThrowIfNull(intent);
        ArgumentNullException.ThrowIfNull(domainEvent);

        var message = Map(intent, domainEvent);
        if (message is null)
        {
            return;
        }

        // LA CLE DE PARTITION EST LA LIVRAISON, pas l'intention : le contrat le
        // fixe, et c'est ce qui garantit a Delivery de lire les faits d'une
        // meme course dans l'ordre ou ils se sont produits.
        outbox.Enqueue(
            KafkaTopics.PaymentEvents,
            intent.DeliveryId.ToString(),
            message.Envelope.EventType,
            message);
    }

    private static PaymentEvent? Map(DomainIntent intent, IDomainEvent domainEvent) => domainEvent switch
    {
        DomainEvents.PaymentIntentSucceeded e => Wrap(intent, e, "hba.payment.v1.PaymentSucceeded", m => m.Succeeded = new PaymentSucceeded
        {
            PaymentIntentId = e.PaymentIntentId.ToString(),
            DeliveryId = e.DeliveryId.ToString(),
            Amount = MapMoney(e.Amount.Amount),
            SucceededAt = Timestamp.FromDateTimeOffset(e.OccurredAt),
        }),

        DomainEvents.PaymentIntentFailed e => Wrap(intent, e, "hba.payment.v1.PaymentFailed", m => m.Failed = new PaymentFailed
        {
            PaymentIntentId = e.PaymentIntentId.ToString(),
            DeliveryId = e.DeliveryId.ToString(),
            Reason = e.Reason,
            OccurredAt = Timestamp.FromDateTimeOffset(e.OccurredAt),
        }),

        // « PaymentRefunded » ETAIT DECLARE DANS LE CONTRAT ET N'AVAIT AUCUN
        // PRODUCTEUR. Il en a un maintenant, et il ne nait pas d'une commande :
        // FedaPay n'a pas d'API de remboursement, donc ce fait est CONSTATE a la
        // relecture de la transaction.
        //
        // LE MONTANT N'EST RENSEIGNE QUE POUR UN REMBOURSEMENT TOTAL. Le statut
        // du fournisseur dit « partiellement rembourse » sans dire combien :
        // ecrire le total dans ce cas ferait croire a un remboursement complet,
        // et inventer une somme serait pire. Le drapeau porte l'incertitude.
        DomainEvents.PaymentIntentRefunded e => Wrap(intent, e, "hba.payment.v1.PaymentRefunded", m => m.Refunded = new PaymentRefunded
        {
            // PAS D'IDENTIFIANT DE REMBOURSEMENT : il n'y a pas d'objet
            // « remboursement » de notre cote, et le fournisseur ne nous en
            // donne pas par ce chemin. L'intention suffit a retrouver la course.
            RefundId = string.Empty,
            PaymentIntentId = e.PaymentIntentId.ToString(),
            DeliveryId = e.DeliveryId.ToString(),
            Amount = e.Partiel ? null : MapMoney(e.Amount.Amount),
            Partial = e.Partiel,
            OccurredAt = Timestamp.FromDateTimeOffset(e.OccurredAt),
        }),

        _ => null,
    };

    private static PaymentEvent Wrap(
        DomainIntent intent,
        IDomainEvent domainEvent,
        string eventType,
        Action<PaymentEvent> fill)
    {
        var message = new PaymentEvent
        {
            Envelope = new EventEnvelope
            {
                EventId = domainEvent.EventId.ToString(),
                EventType = eventType,
                SchemaVersion = 1,
                AggregateType = AggregateType,
                AggregateId = intent.Id.ToString(),
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

    private static Money MapMoney(long amount)
        => new() { Amount = amount, Currency = Domain.ValueObjects.MoneyXof.CurrencyCode };

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
