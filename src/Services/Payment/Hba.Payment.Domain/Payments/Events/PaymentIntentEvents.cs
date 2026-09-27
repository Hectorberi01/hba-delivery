using Hba.BuildingBlocks.Domain;
using Hba.Payment.Domain.ValueObjects;

namespace Hba.Payment.Domain.Payments.Events;

/// <summary>
/// L'argent est arrive. C'EST LE SEUL FAIT QUI FAIT PASSER UNE LIVRAISON EN
/// PAID : Delivery n'ecoute rien d'autre, et surtout pas le retour de
/// l'application cliente apres la page de paiement.
/// </summary>
public sealed record PaymentIntentSucceeded(
    Guid PaymentIntentId,
    Guid DeliveryId,
    MoneyXof Amount,
    Actor Actor,
    DateTimeOffset OccurredAt) : DomainEvent(Actor, OccurredAt);

public sealed record PaymentIntentFailed(
    Guid PaymentIntentId,
    Guid DeliveryId,
    string Reason,
    Actor Actor,
    DateTimeOffset OccurredAt) : DomainEvent(Actor, OccurredAt);
