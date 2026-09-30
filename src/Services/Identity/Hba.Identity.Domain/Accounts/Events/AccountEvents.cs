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

/// <summary>
/// Le titulaire a demandé la suppression de son compte.
///
/// IL NE SORT PAS D'IDENTITY, ET C'EST VOULU. Rien n'est encore effacé : le
/// compte est en sursis, et le titulaire peut revenir dessus. Prévenir les
/// autres services maintenant les ferait agir sur une décision qui n'est pas
/// prise — ou, pire, les laisserait dans un état intermédiaire qu'aucun
/// événement d'annulation ne rattraperait proprement. Ce qui sort, c'est
/// <see cref="AccountErased" />, et seulement au terme.
/// </summary>
public sealed record AccountDeletionRequested(
    Guid AccountId,
    DateTimeOffset ScheduledFor,
    Actor Actor,
    DateTimeOffset OccurredAt) : AccountDomainEvent(AccountId, Actor, OccurredAt);

/// <summary>Le titulaire est revenu sur sa demande.</summary>
public sealed record AccountDeletionCancelled(
    Guid AccountId,
    Actor Actor,
    DateTimeOffset OccurredAt) : AccountDomainEvent(AccountId, Actor, OccurredAt);

/// <summary>
/// Le compte est effacé pour de bon.
///
/// C'EST LE SEUL DES TROIS QUI QUITTE IDENTITY. Directory y efface le profil et
/// les adresses, et fait effacer la photo par Media. Payment et Notification ne
/// l'écoutent pas : voir le point 28 des points à trancher, qui dit pourquoi et
/// ce qu'il reste à vérifier.
///
/// IL NE PORTE NI NOM NI TELEPHONE. Un événement de suppression qui recopierait
/// l'identité du titulaire la ferait vivre dans Kafka pendant toute la rétention
/// du topic, au moment précis où quelqu'un demande qu'elle disparaisse.
/// </summary>
public sealed record AccountErased(
    Guid AccountId,
    DateTimeOffset RequestedAt,
    Actor Actor,
    DateTimeOffset OccurredAt) : AccountDomainEvent(AccountId, Actor, OccurredAt);
