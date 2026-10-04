using Hba.BuildingBlocks.Domain;

namespace Hba.Dispatch.Domain.Dispatching.Events;

/// <summary>
/// Socle des faits du moteur de dispatch.
/// </summary>
///
/// <remarks>
/// LE COUPLE (ReferenceId, ServiceType) REMPLACE L'ANCIEN DeliveryId, et les deux
/// ne se lisent jamais l'un sans l'autre. Un consommateur qui agirait sur
/// l'identifiant sans regarder la nature traiterait une course comme une
/// livraison : introuvable en base, trois reessais, message abandonne — sur une
/// mission pourtant correcte.
/// </remarks>
public abstract record DispatchDomainEvent(
    Guid ReferenceId,
    ServiceType ServiceType,
    Actor Actor,
    DateTimeOffset OccurredAt) : DomainEvent(Actor, OccurredAt);

public sealed record OfferSent(
    Guid ReferenceId,
    ServiceType ServiceType,
    Guid OfferId,
    string DriverId,
    int WaveNumber,
    DateTimeOffset ExpiresAt,
    Actor Actor,
    DateTimeOffset OccurredAt) : DispatchDomainEvent(ReferenceId, ServiceType, Actor, OccurredAt);

public sealed record OfferAccepted(
    Guid ReferenceId,
    ServiceType ServiceType,
    Guid OfferId,
    string DriverId,
    Actor Actor,
    DateTimeOffset OccurredAt) : DispatchDomainEvent(ReferenceId, ServiceType, Actor, OccurredAt);

/// <summary>
/// Une offre s'est eteinte sans reponse. ELLE SORT DU SERVICE parce que Driver
/// doit rendre le livreur disponible : sans cet evenement il resterait
/// RESERVED, donc invisible pour toutes les vagues suivantes.
/// </summary>
public sealed record OfferExpired(
    Guid ReferenceId,
    ServiceType ServiceType,
    Guid OfferId,
    string DriverId,
    Actor Actor,
    DateTimeOffset OccurredAt) : DispatchDomainEvent(ReferenceId, ServiceType, Actor, OccurredAt);

/// <summary>
/// Offre rendue caduque par l'acceptation d'un autre livreur de la meme vague.
/// Meme raison de sortir que l'expiration : liberer le livreur.
/// </summary>
public sealed record OfferSuperseded(
    Guid ReferenceId,
    ServiceType ServiceType,
    Guid OfferId,
    string DriverId,
    Actor Actor,
    DateTimeOffset OccurredAt) : DispatchDomainEvent(ReferenceId, ServiceType, Actor, OccurredAt);

public sealed record OfferDeclined(
    Guid ReferenceId,
    ServiceType ServiceType,
    Guid OfferId,
    string DriverId,
    string? Reason,
    Actor Actor,
    DateTimeOffset OccurredAt) : DispatchDomainEvent(ReferenceId, ServiceType, Actor, OccurredAt);

public sealed record DispatchExhausted(
    Guid ReferenceId,
    ServiceType ServiceType,
    int WavesAttempted,
    Actor Actor,
    DateTimeOffset OccurredAt) : DispatchDomainEvent(ReferenceId, ServiceType, Actor, OccurredAt);
