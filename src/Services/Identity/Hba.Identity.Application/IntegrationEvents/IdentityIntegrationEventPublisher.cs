using Google.Protobuf.WellKnownTypes;
using Hba.BuildingBlocks.Domain;
using Hba.BuildingBlocks.Messaging.Kafka;
using Hba.BuildingBlocks.Messaging.Outbox;
using Hba.Contracts.Common.V1;
using Hba.Contracts.Identity.V1;
using Hba.Identity.Domain.Partners;
// Account, AccountStatus et PartnerClientRegistered existent des deux côtés :
// comme message protobuf et comme type du domaine. Les alias tranchent une fois
// pour toutes, plutôt que de qualifier chaque usage.
using AccountAggregate = Hba.Identity.Domain.Accounts.Account;
using DomainEvents = Hba.Identity.Domain.Accounts.Events;
using PartnerDomainEvents = Hba.Identity.Domain.Partners.Events;

namespace Hba.Identity.Application.IntegrationEvents;

public interface IIdentityIntegrationEventPublisher
{
    void Publish(IOutbox outbox, AccountAggregate account, IDomainEvent domainEvent);

    /// <summary>
    /// Un client partenaire est un agrégat distinct : ce n'est pas un compte de
    /// personne, il n'a ni téléphone ni mot de passe.
    /// </summary>
    void Publish(IOutbox outbox, PartnerClient partner, IDomainEvent domainEvent);
}

/// L'Outbox est passée en paramètre plutôt qu'injectée : EfOutbox écrit dans le
/// DbContext, et le DbContext a besoin de ce publieur. Les injecter tous les
/// deux formerait un cycle que le conteneur refuse de résoudre — y compris au
/// moment où dotnet ef instancie le contexte.
public sealed class IdentityIntegrationEventPublisher : IIdentityIntegrationEventPublisher
{
    private const string Producer = "identity";

    public void Publish(IOutbox outbox, AccountAggregate account, IDomainEvent domainEvent)
    {
        ArgumentNullException.ThrowIfNull(outbox);
        ArgumentNullException.ThrowIfNull(account);
        ArgumentNullException.ThrowIfNull(domainEvent);

        var message = Map(account, domainEvent);

        if (message is null)
        {
            return;
        }

        outbox.Enqueue(
            KafkaTopics.IdentityEvents,
            account.Id.ToString(),
            message.Envelope.EventType,
            message);
    }

    public void Publish(IOutbox outbox, PartnerClient partner, IDomainEvent domainEvent)
    {
        ArgumentNullException.ThrowIfNull(outbox);
        ArgumentNullException.ThrowIfNull(partner);
        ArgumentNullException.ThrowIfNull(domainEvent);

        // La rotation d'un secret est auditée en base, mais ne regarde aucun
        // autre service : rien ne sort du topic pour elle.
        if (domainEvent is not PartnerDomainEvents.PartnerClientRegistered registered)
        {
            return;
        }

        var message = new IdentityEvent
        {
            Envelope = Envelope(
                registered.EventId,
                "hba.identity.v1.PartnerClientRegistered",
                "partner_client",
                partner.Id.ToString(),
                registered.OccurredAt,
                registered.Actor,
                partner.Id.ToString()),
            PartnerRegistered = new PartnerClientRegistered
            {
                PartnerId = partner.Id.ToString(),
                PartnerName = registered.Name,
                Source = registered.Source,
                OccurredAt = Timestamp.FromDateTimeOffset(registered.OccurredAt),
            },
        };

        outbox.Enqueue(
            KafkaTopics.IdentityEvents,
            partner.Id.ToString(),
            message.Envelope.EventType,
            message);
    }

    private static IdentityEvent? Map(AccountAggregate account, IDomainEvent domainEvent) => domainEvent switch
    {
        DomainEvents.AccountRegistered e => new IdentityEvent
        {
            Envelope = Envelope(e.EventId, "hba.identity.v1.AccountRegistered", account, e),
            Registered = BuildRegistered(e),
        },

        DomainEvents.AccountRolesChanged e => new IdentityEvent
        {
            Envelope = Envelope(e.EventId, "hba.identity.v1.AccountRolesChanged", account, e),
            RolesChanged = BuildRolesChanged(e),
        },

        DomainEvents.AccountStatusChanged e => new IdentityEvent
        {
            Envelope = Envelope(e.EventId, "hba.identity.v1.AccountStatusChanged", account, e),
            StatusChanged = BuildStatusChanged(e),
        },

        // Un changement de mot de passe et un rattachement de profil livreur ne
        // concernent aucun autre service aujourd'hui : rien ne sort.
        _ => null,
    };

    private static AccountRegistered BuildRegistered(DomainEvents.AccountRegistered e)
    {
        var message = new AccountRegistered
        {
            AccountId = e.AccountId.ToString(),
            Phone = e.Phone ?? string.Empty,
            Email = e.Email ?? string.Empty,
            DisplayName = e.DisplayName,
            MerchantId = e.MerchantId ?? string.Empty,
            RegisteredAt = Timestamp.FromDateTimeOffset(e.OccurredAt),
        };

        message.Roles.AddRange(e.Roles);
        return message;
    }

    private static AccountRolesChanged BuildRolesChanged(DomainEvents.AccountRolesChanged e)
    {
        var message = new AccountRolesChanged
        {
            AccountId = e.AccountId.ToString(),
            ChangedBy = e.Actor.Id,
            Reason = e.Reason,
            OccurredAt = Timestamp.FromDateTimeOffset(e.OccurredAt),
        };

        message.PreviousRoles.AddRange(e.PreviousRoles);
        message.CurrentRoles.AddRange(e.CurrentRoles);
        return message;
    }

    private static AccountStatusChanged BuildStatusChanged(DomainEvents.AccountStatusChanged e)
        => new()
        {
            AccountId = e.AccountId.ToString(),
            Previous = MapStatus(e.Previous),
            Current = MapStatus(e.Current),
            ChangedBy = e.Actor.Id,
            Reason = e.Reason,
            OccurredAt = Timestamp.FromDateTimeOffset(e.OccurredAt),
        };

    private static EventEnvelope Envelope(
        Guid eventId,
        string eventType,
        AccountAggregate account,
        IDomainEvent domainEvent)
        => Envelope(
            eventId,
            eventType,
            "account",
            account.Id.ToString(),
            domainEvent.OccurredAt,
            domainEvent.Actor,
            partnerId: string.Empty);

    private static EventEnvelope Envelope(
        Guid eventId,
        string eventType,
        string aggregateType,
        string aggregateId,
        DateTimeOffset occurredAt,
        Actor actor,
        string partnerId)
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
            PartnerId = partnerId,
            ActorType = MapActor(actor.Kind),
            ActorId = actor.Id,
            Producer = Producer,
        };

    private static Contracts.Identity.V1.AccountStatus MapStatus(Domain.Accounts.AccountStatus status) => status switch
    {
        Domain.Accounts.AccountStatus.Active => Contracts.Identity.V1.AccountStatus.Active,
        Domain.Accounts.AccountStatus.Suspended => Contracts.Identity.V1.AccountStatus.Suspended,
        _ => Contracts.Identity.V1.AccountStatus.Unspecified,
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
