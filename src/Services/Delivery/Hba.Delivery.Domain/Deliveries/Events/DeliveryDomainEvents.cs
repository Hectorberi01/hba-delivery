using Hba.BuildingBlocks.Domain;
using Hba.Delivery.Domain.ValueObjects;

namespace Hba.Delivery.Domain.Deliveries.Events;

/// <summary>
/// Faits métier de l'agrégat Delivery. Ils restent internes au service ; la
/// couche Application décide lesquels deviennent des messages Kafka.
/// </summary>
public abstract record DeliveryDomainEvent(Guid DeliveryId, Actor Actor, DateTimeOffset OccurredAt)
    : DomainEvent(Actor, OccurredAt);

public sealed record DeliveryCreated(
    Guid DeliveryId,
    string Reference,
    DeliverySource Source,
    string? CustomerId,
    string? MerchantId,
    MoneyXof Total,
    Actor Actor,
    DateTimeOffset OccurredAt) : DeliveryDomainEvent(DeliveryId, Actor, OccurredAt);

/// <summary>
/// Paiement confirmé par FedaPay. C'est cet événement, et lui seul, qui autorise
/// la recherche d'un livreur.
/// </summary>
public sealed record DeliveryConfirmed(
    Guid DeliveryId,
    string PaymentIntentId,
    MoneyXof Total,
    Actor Actor,
    DateTimeOffset OccurredAt) : DeliveryDomainEvent(DeliveryId, Actor, OccurredAt);

public sealed record DeliveryPaymentFailed(
    Guid DeliveryId,
    string Reason,
    Actor Actor,
    DateTimeOffset OccurredAt) : DeliveryDomainEvent(DeliveryId, Actor, OccurredAt);

public sealed record DriverSearchStarted(
    Guid DeliveryId,
    Actor Actor,
    DateTimeOffset OccurredAt) : DeliveryDomainEvent(DeliveryId, Actor, OccurredAt);

public sealed record DriverAssigned(
    Guid DeliveryId,
    string DriverId,
    string OfferId,
    Actor Actor,
    DateTimeOffset OccurredAt) : DeliveryDomainEvent(DeliveryId, Actor, OccurredAt);

public sealed record DriverUnassigned(
    Guid DeliveryId,
    string PreviousDriverId,
    string Reason,
    Actor Actor,
    DateTimeOffset OccurredAt) : DeliveryDomainEvent(DeliveryId, Actor, OccurredAt);

public sealed record DriverArrivedAtPickup(
    Guid DeliveryId,
    string DriverId,
    Actor Actor,
    DateTimeOffset OccurredAt) : DeliveryDomainEvent(DeliveryId, Actor, OccurredAt);

public sealed record DeliveryPickedUp(
    Guid DeliveryId,
    string DriverId,
    string? ProofObjectKey,
    Actor Actor,
    DateTimeOffset OccurredAt) : DeliveryDomainEvent(DeliveryId, Actor, OccurredAt);

public sealed record DeliveryCompleted(
    Guid DeliveryId,
    string DriverId,
    MoneyXof DriverEarning,
    string? ProofObjectKey,
    Actor Actor,
    DateTimeOffset OccurredAt) : DeliveryDomainEvent(DeliveryId, Actor, OccurredAt);

public sealed record DeliveryCancelled(
    Guid DeliveryId,
    DeliveryStatus PreviousStatus,
    string Reason,
    Actor Actor,
    DateTimeOffset OccurredAt) : DeliveryDomainEvent(DeliveryId, Actor, OccurredAt);

public sealed record DeliveryFailed(
    Guid DeliveryId,
    DeliveryStatus PreviousStatus,
    string Reason,
    Actor Actor,
    DateTimeOffset OccurredAt) : DeliveryDomainEvent(DeliveryId, Actor, OccurredAt);

public sealed record NoDriverFound(
    Guid DeliveryId,
    int WavesAttempted,
    Actor Actor,
    DateTimeOffset OccurredAt) : DeliveryDomainEvent(DeliveryId, Actor, OccurredAt);

/// <summary>
/// Échec de saisie du code de remise. Sans trace, on ne distingue pas un livreur
/// qui se trompe d'un livreur qui essaie de forcer.
/// </summary>
public sealed record DeliveryOtpAttemptFailed(
    Guid DeliveryId,
    string DriverId,
    int FailedAttempts,
    Actor Actor,
    DateTimeOffset OccurredAt) : DeliveryDomainEvent(DeliveryId, Actor, OccurredAt);
