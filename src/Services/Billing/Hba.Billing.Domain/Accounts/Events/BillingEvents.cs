using Hba.BuildingBlocks.Domain;

namespace Hba.Billing.Domain.Accounts.Events;

/// <summary>
/// Faits métier de l'agrégat BillingAccount. Ils restent internes au service ;
/// la couche Application décidera lesquels deviennent des messages Kafka.
/// </summary>
public abstract record BillingDomainEvent(Guid AccountId, Actor Actor, DateTimeOffset OccurredAt)
    : DomainEvent(Actor, OccurredAt);

/// <summary>Un compte de facturation a été ouvert.</summary>
public sealed record BillingAccountOpened(
    Guid AccountId,
    string OwnerType,
    string OwnerId,
    SettlementMode Mode,
    Actor Actor,
    DateTimeOffset OccurredAt) : BillingDomainEvent(AccountId, Actor, OccurredAt);

/// <summary>Une course a été débitée du compte.</summary>
public sealed record AccountDebited(
    Guid AccountId,
    Guid MovementId,
    long AmountXof,
    long BalanceAfterXof,
    string Reference,
    Actor Actor,
    DateTimeOffset OccurredAt) : BillingDomainEvent(AccountId, Actor, OccurredAt);

/// <summary>Le compte a été crédité : recharge, remboursement ou règlement.</summary>
public sealed record AccountCredited(
    Guid AccountId,
    Guid MovementId,
    MovementKind Kind,
    long AmountXof,
    long BalanceAfterXof,
    string Reference,
    Actor Actor,
    DateTimeOffset OccurredAt) : BillingDomainEvent(AccountId, Actor, OccurredAt);

/// <summary>
/// Le solde est passé sous le seuil d'alerte.
/// </summary>
///
/// <remarks>
/// AU FRANCHISSEMENT, ET PAS À CHAQUE COURSE SOUS LE SEUIL. Un titulaire qui
/// reçoit trente messages par jour n'en lit aucun — et le jour où le compte
/// tombe vraiment à sec, le message qui le disait ressemblait aux vingt-neuf
/// autres. Le domaine compare le solde AVANT et APRÈS : il n'émet que si la
/// ligne a été franchie par CE mouvement, ce qui ne demande aucun état de plus.
/// </remarks>
public sealed record LowBalanceReached(
    Guid AccountId,
    long BalanceXof,
    long ThresholdXof,
    Actor Actor,
    DateTimeOffset OccurredAt) : BillingDomainEvent(AccountId, Actor, OccurredAt);

/// <summary>
/// Un plafond de crédit a été accordé — ou retiré, s'il retombe à zéro.
/// </summary>
///
/// <remarks>
/// C'EST LE SEUL GESTE QUI FAIT PASSER UN COMPTE EN POSTPAYÉ, et il engage de
/// l'argent que HBA avance. Il est donc audité comme une transition d'état,
/// avec son acteur : le jour où un encours dérape, la question posée sera
/// « qui a accordé ce plafond, et quand ».
/// </remarks>
public sealed record CreditLimitGranted(
    Guid AccountId,
    long CreditLimitXof,
    SettlementMode Mode,
    Actor Actor,
    DateTimeOffset OccurredAt) : BillingDomainEvent(AccountId, Actor, OccurredAt);

/// <summary>Le compte a été suspendu ou réactivé par le back-office.</summary>
public sealed record AccountStatusChanged(
    Guid AccountId,
    AccountStatus Status,
    Actor Actor,
    DateTimeOffset OccurredAt) : BillingDomainEvent(AccountId, Actor, OccurredAt);
