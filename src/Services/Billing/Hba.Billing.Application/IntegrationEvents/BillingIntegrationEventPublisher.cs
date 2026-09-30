using Google.Protobuf.WellKnownTypes;
using Hba.Billing.Application.Authorization;
using Hba.Billing.Domain.Accounts;
using Hba.Billing.Domain.ValueObjects;
using Hba.BuildingBlocks.Domain;
using Hba.BuildingBlocks.Messaging.Kafka;
using Hba.BuildingBlocks.Messaging.Outbox;
using Hba.Contracts.Billing.V1;
using Hba.Contracts.Common.V1;
using CommonMoney = Hba.Contracts.Common.V1.Money;

// « LowBalanceReached » EXISTE DANS LES DEUX MONDES : le fait de domaine et le
// message proto portent le meme nom, ce qui est voulu — c'est le meme fait. Sans
// cet alias, chaque mention serait ambigue et le compilateur refuserait le
// fichier ; le qualifier partout le rendrait illisible.
using DomainEvents = Hba.Billing.Domain.Accounts.Events;

namespace Hba.Billing.Application.IntegrationEvents;

/// <summary>
/// Traduit les faits de domaine de Billing en messages Kafka, dans l'Outbox.
/// </summary>
public interface IBillingIntegrationEventPublisher
{
    void Publish(IOutbox outbox, BillingAccount account, IDomainEvent domainEvent);
}

/// <summary>
/// UN SEUL FAIT SORT DE CE SERVICE : « LowBalanceReached ».
/// </summary>
///
/// <remarks>
/// LES DÉBITS ET LES CRÉDITS RESTENT DEDANS, ET C'EST DÉLIBÉRÉ. Personne ne les
/// consomme : publier un flux comptable que rien ne lit coûterait un topic, une
/// rétention et surtout un contrat qu'il faudrait ensuite ne plus casser, pour
/// donner l'illusion d'une intégration en place. Ils s'ajouteront le jour où un
/// grand livre ou un rapprochement les demandera — le mécanisme est ici.
///
/// L'ALERTE DE SOLDE, ELLE, A UN DESTINATAIRE ÉVIDENT : le titulaire du compte,
/// qui doit recharger AVANT qu'une course ne soit refusée. Elle était levée par
/// le domaine, testée, et ne sortait nulle part.
///
/// CE QUI MANQUE ENCORE, ET QU'IL FAUT SAVOIR : aucun consommateur ne l'écoute.
/// Prévenir le titulaire suppose de savoir QUI joindre, et Billing ne connaît
/// qu'un « merchant_id » ou un « partner_id » — pas un compte, pas un numéro.
/// Ni SendSms (qui veut un numéro) ni SendEmail (qui veut un sujet Identity) ne
/// se remplissent depuis ce service. Le choix du contact est une décision, pas
/// une déduction : voir points-a-trancher. L'événement part quand même, parce
/// qu'une fois publié il est RATTRAPABLE — un consommateur ajouté plus tard lira
/// la rétention du topic — alors qu'un fait jamais émis est perdu.
///
/// L'Outbox est passée en paramètre plutôt qu'injectée : EfOutbox écrit dans le
/// DbContext, et le DbContext a besoin de ce publieur. Les injecter tous les
/// deux formerait un cycle que le conteneur refuse de résoudre.
/// </remarks>
public sealed class BillingIntegrationEventPublisher : IBillingIntegrationEventPublisher
{
    private const string Producer = "billing";
    private const string AggregateType = "billing_account";

    public void Publish(IOutbox outbox, BillingAccount account, IDomainEvent domainEvent)
    {
        ArgumentNullException.ThrowIfNull(outbox);
        ArgumentNullException.ThrowIfNull(account);
        ArgumentNullException.ThrowIfNull(domainEvent);

        if (domainEvent is not DomainEvents.LowBalanceReached seuil)
        {
            return;
        }

        var message = new BillingEvent
        {
            Envelope = Enveloppe(account, domainEvent, "hba.billing.v1.LowBalanceReached"),
            LowBalanceReached = new Contracts.Billing.V1.LowBalanceReached
            {
                AccountId = account.Id.ToString(),
                OwnerType = account.OwnerType,
                OwnerId = account.OwnerId,
                Balance = Montant(seuil.BalanceXof),
                Threshold = Montant(seuil.ThresholdXof),
                OccurredAt = Timestamp.FromDateTimeOffset(seuil.OccurredAt),
            },
        };

        // CLE DE PARTITION : LE TITULAIRE. Deux alertes du même compte arrivent
        // dans l'ordre où le solde est descendu. L'identifiant technique du
        // compte ferait la même chose, mais obligerait le consommateur à le
        // connaître pour s'y retrouver.
        outbox.Enqueue(
            KafkaTopics.BillingEvents,
            $"{account.OwnerType}:{account.OwnerId}",
            message.Envelope.EventType,
            message);
    }

    private static EventEnvelope Enveloppe(
        BillingAccount account,
        IDomainEvent domainEvent,
        string eventType)
        => new()
        {
            EventId = domainEvent.EventId.ToString(),
            EventType = eventType,
            SchemaVersion = 1,
            AggregateType = AggregateType,
            AggregateId = account.Id.ToString(),
            OccurredAt = Timestamp.FromDateTimeOffset(domainEvent.OccurredAt),
            TraceId = System.Diagnostics.Activity.Current?.TraceId.ToString() ?? string.Empty,
            CorrelationId = System.Diagnostics.Activity.Current?.RootId ?? string.Empty,

            // LE CLOISONNEMENT N'EST RENSEIGNE QUE POUR UN COMPTE DE PARTENAIRE.
            // Un compte de commerçant appartient à HBA en propre : y écrire un
            // partenaire qu'on ne connaît pas serait une invention, et
            // l'enveloppe sert justement à cloisonner.
            PartnerId = string.Equals(account.OwnerType, BillingOwnerTypes.Partner, StringComparison.Ordinal)
                ? account.OwnerId
                : string.Empty,

            ActorType = MapActor(domainEvent.Actor.Kind),
            ActorId = domainEvent.Actor.Id,
            Producer = Producer,
        };

    private static CommonMoney Montant(long francs)
        => new() { Amount = francs, Currency = MoneyXof.CurrencyCode };

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
