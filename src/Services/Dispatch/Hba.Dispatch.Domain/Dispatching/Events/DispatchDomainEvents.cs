using Hba.BuildingBlocks.Domain;

namespace Hba.Dispatch.Domain.Dispatching.Events;

public abstract record DispatchDomainEvent(Guid DeliveryId, Actor Actor, DateTimeOffset OccurredAt)
    : DomainEvent(Actor, OccurredAt);

public sealed record OfferSent(
    Guid DeliveryId,
    Guid OfferId,
    string DriverId,
    int WaveNumber,
    DateTimeOffset ExpiresAt,
    Actor Actor,
    DateTimeOffset OccurredAt) : DispatchDomainEvent(DeliveryId, Actor, OccurredAt);

public sealed record OfferAccepted(
    Guid DeliveryId,
    Guid OfferId,
    string DriverId,
    Actor Actor,
    DateTimeOffset OccurredAt) : DispatchDomainEvent(DeliveryId, Actor, OccurredAt);

/// <summary>
/// Une offre s'est eteinte sans reponse. ELLE SORT DU SERVICE parce que Driver
/// doit rendre le livreur disponible : sans cet evenement il resterait
/// RESERVED, donc invisible pour toutes les vagues suivantes.
/// </summary>
public sealed record OfferExpired(
    Guid DeliveryId,
    Guid OfferId,
    string DriverId,
    Actor Actor,
    DateTimeOffset OccurredAt) : DispatchDomainEvent(DeliveryId, Actor, OccurredAt);

/// <summary>
/// Offre rendue caduque par l'acceptation d'un autre livreur de la meme vague.
/// Meme raison de sortir que l'expiration : liberer le livreur.
/// </summary>
public sealed record OfferSuperseded(
    Guid DeliveryId,
    Guid OfferId,
    string DriverId,
    Actor Actor,
    DateTimeOffset OccurredAt) : DispatchDomainEvent(DeliveryId, Actor, OccurredAt);

public sealed record OfferDeclined(
    Guid DeliveryId,
    Guid OfferId,
    string DriverId,
    string? Reason,
    Actor Actor,
    DateTimeOffset OccurredAt) : DispatchDomainEvent(DeliveryId, Actor, OccurredAt);

public sealed record DispatchExhausted(
    Guid DeliveryId,
    int WavesAttempted,
    Actor Actor,
    DateTimeOffset OccurredAt) : DispatchDomainEvent(DeliveryId, Actor, OccurredAt);
