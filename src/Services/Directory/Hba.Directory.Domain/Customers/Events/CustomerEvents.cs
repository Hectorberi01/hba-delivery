using Hba.BuildingBlocks.Domain;

namespace Hba.Directory.Domain.Customers.Events;

public abstract record CustomerDomainEvent(Guid CustomerId, Actor Actor, DateTimeOffset OccurredAt)
    : DomainEvent(Actor, OccurredAt);

public sealed record CustomerProfileCreated(
    Guid CustomerId,
    string Phone,
    Actor Actor,
    DateTimeOffset OccurredAt) : CustomerDomainEvent(CustomerId, Actor, OccurredAt);

public sealed record CustomerProfileUpdated(
    Guid CustomerId,
    Actor Actor,
    DateTimeOffset OccurredAt) : CustomerDomainEvent(CustomerId, Actor, OccurredAt);
