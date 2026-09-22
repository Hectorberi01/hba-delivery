using Hba.BuildingBlocks.Domain;

namespace Hba.Identity.Domain.Partners.Events;

public abstract record PartnerDomainEvent(Guid PartnerId, Actor Actor, DateTimeOffset OccurredAt)
    : DomainEvent(Actor, OccurredAt);

public sealed record PartnerClientRegistered(
    Guid PartnerId,
    string Name,
    string Source,
    Actor Actor,
    DateTimeOffset OccurredAt) : PartnerDomainEvent(PartnerId, Actor, OccurredAt);

public sealed record PartnerSecretRotated(
    Guid PartnerId,
    string Reason,
    Actor Actor,
    DateTimeOffset OccurredAt) : PartnerDomainEvent(PartnerId, Actor, OccurredAt);
