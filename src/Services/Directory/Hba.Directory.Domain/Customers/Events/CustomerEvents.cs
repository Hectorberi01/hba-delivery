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

/// <summary>
/// La photo de profil du client a changé — posée, remplacée ou retirée.
/// </summary>
///
/// <remarks>
/// UN SEUL EVENEMENT POUR LES TROIS GESTES, avec un identifiant qui peut être
/// nul. Trois événements distincts — posée, remplacée, retirée — auraient
/// obligé chaque lecteur à les traiter ensemble pour répondre à la seule
/// question qu'on leur posera jamais : « quelle photo porte ce client
/// maintenant ? ». Et le jour où l'un des trois aurait été oublié quelque part,
/// un client se serait promené avec un portrait qu'il croyait avoir effacé.
/// </remarks>
public sealed record CustomerPhotoChanged(
    Guid CustomerId,
    Guid? PhotoMediaId,
    Actor Actor,
    DateTimeOffset OccurredAt) : CustomerDomainEvent(CustomerId, Actor, OccurredAt);
