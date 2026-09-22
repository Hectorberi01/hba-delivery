using Hba.BuildingBlocks.Domain;
using Hba.Directory.Domain.ValueObjects;

namespace Hba.Directory.Domain.Merchants.Events;

public abstract record MerchantDomainEvent(Guid MerchantId, Actor Actor, DateTimeOffset OccurredAt)
    : DomainEvent(Actor, OccurredAt);

public sealed record MerchantCreated(
    Guid MerchantId,
    string LegalName,
    Actor Actor,
    DateTimeOffset OccurredAt) : MerchantDomainEvent(MerchantId, Actor, OccurredAt);

public sealed record MerchantUpdated(
    Guid MerchantId,
    Actor Actor,
    DateTimeOffset OccurredAt) : MerchantDomainEvent(MerchantId, Actor, OccurredAt);

public sealed record MerchantDeactivated(
    Guid MerchantId,
    string Reason,
    Actor Actor,
    DateTimeOffset OccurredAt) : MerchantDomainEvent(MerchantId, Actor, OccurredAt);

/// <summary>
/// Un point de collecte qui bouge ou qui ferme concerne Dispatch : un livreur ne
/// doit pas être envoyé à une adresse qui n'est plus la bonne.
/// </summary>
public sealed record PickupPointChanged(
    Guid MerchantId,
    Guid PickupPointId,
    Address Address,
    bool IsActive,
    Actor Actor,
    DateTimeOffset OccurredAt) : MerchantDomainEvent(MerchantId, Actor, OccurredAt);
