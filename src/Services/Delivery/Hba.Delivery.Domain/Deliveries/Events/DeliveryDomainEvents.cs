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

/// <summary>
/// Course B2B réglée par le compte de son donneur d'ordre.
/// </summary>
///
/// <remarks>
/// IL OUVRE LA RECHERCHE D'UN LIVREUR, AU MEME TITRE QUE « DeliveryConfirmed »,
/// et il est pourtant distinct de lui. La raison tient en une phrase : l'un dit
/// « FedaPay a encaissé », l'autre « le compte du commerçant a réglé ». Aucun
/// argent n'a circulé ici ; le faire passer pour un encaissement mettrait un
/// mensonge dans la seule trace qu'on relira le jour d'un litige.
///
/// SUR LE FIL, LES DEUX DEVIENNENT LE MEME MESSAGE. Le contrat
/// « hba.delivery.v1.DeliveryConfirmed » ne porte aucune référence de paiement
/// — Dispatch n'a besoin que du point d'enlèvement, de la distance et du gain
/// du livreur. La distinction reste donc intérieure au service, là où elle sert.
/// </remarks>
public sealed record DeliverySettledByAccount(
    Guid DeliveryId,
    string MovementId,
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
    Actor Actor,
    DateTimeOffset OccurredAt) : DeliveryDomainEvent(DeliveryId, Actor, OccurredAt);

public sealed record DeliveryCompleted(
    Guid DeliveryId,
    string DriverId,
    MoneyXof DriverEarning,
    Actor Actor,
    DateTimeOffset OccurredAt) : DeliveryDomainEvent(DeliveryId, Actor, OccurredAt);

/// <summary>
/// Une photo a été rattachée à une étape de la course.
/// </summary>
///
/// <remarks>
/// UN FAIT SEPARE, PARCE QUE C'EST UN GESTE SEPARE. La photo ne voyage plus avec
/// « DeliveryPickedUp » ni « DeliveryCompleted » : depuis la décision du
/// 30 septembre 2026 (point 7), l'étape ne l'attend jamais, et le dépôt arrive
/// APRÈS, par sa propre route. Un champ qui accompagnait l'étape aurait valu nul
/// à chaque fois, et un champ toujours nul finit par être lu comme « il n'y a
/// pas de photo » plutôt que « elle n'est pas encore arrivée ».
///
/// IL NE SORT PAS DU SERVICE aujourd'hui : aucun événement d'intégration ne le
/// porte. Il est là pour le journal des faits de l'agrégat — savoir qu'une pièce
/// a été jointe, par qui et quand, est exactement ce qu'on vient chercher le
/// jour d'une réclamation.
/// </remarks>
public sealed record PreuveAttachee(
    Guid DeliveryId,
    string DriverId,
    EtapeDeLaPreuve Etape,
    Guid MediaId,
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

/// <summary>
/// Le remboursement du client a été constaté. NE SORT PAS DU SERVICE : aucun
/// autre n'en a besoin — Payment le sait déjà, c'est lui qui l'a appris.
/// </summary>
public sealed record DeliveryRefunded(
    Guid DeliveryId,
    bool Partial,
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
