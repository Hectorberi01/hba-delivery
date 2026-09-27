using Hba.BuildingBlocks.Domain;

namespace Hba.Identity.Domain.Accounts.Events;

public abstract record AccountDomainEvent(Guid AccountId, Actor Actor, DateTimeOffset OccurredAt)
    : DomainEvent(Actor, OccurredAt);

public sealed record AccountRegistered(
    Guid AccountId,
    string? Phone,
    string? Email,
    string DisplayName,
    IReadOnlyList<string> Roles,
    string? MerchantId,
    Actor Actor,
    DateTimeOffset OccurredAt) : AccountDomainEvent(AccountId, Actor, OccurredAt);

public sealed record AccountRolesChanged(
    Guid AccountId,
    IReadOnlyList<string> PreviousRoles,
    IReadOnlyList<string> CurrentRoles,
    string Reason,
    Actor Actor,
    DateTimeOffset OccurredAt) : AccountDomainEvent(AccountId, Actor, OccurredAt);

public sealed record AccountStatusChanged(
    Guid AccountId,
    AccountStatus Previous,
    AccountStatus Current,
    string Reason,
    Actor Actor,
    DateTimeOffset OccurredAt) : AccountDomainEvent(AccountId, Actor, OccurredAt);

public sealed record AccountPasswordChanged(
    Guid AccountId,
    Actor Actor,
    DateTimeOffset OccurredAt) : AccountDomainEvent(AccountId, Actor, OccurredAt);

public sealed record AccountLinkedToDriver(
    Guid AccountId,
    string DriverId,
    Actor Actor,
    DateTimeOffset OccurredAt) : AccountDomainEvent(AccountId, Actor, OccurredAt);

/// <summary>
/// Le titulaire a accordé ou retiré son consentement à recevoir des messages
/// WhatsApp. Cet événement existe pour que le consentement soit AUDITABLE :
/// Meta peut demander à voir quand et comment il a été recueilli, et « le champ
/// vaut vrai » ne répond pas à cette question.
/// </summary>
public sealed record AccountWhatsAppConsentChanged(
    Guid AccountId,
    bool Granted,
    Actor Actor,
    DateTimeOffset OccurredAt) : AccountDomainEvent(AccountId, Actor, OccurredAt);
