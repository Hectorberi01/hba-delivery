using Hba.BuildingBlocks.Domain;
using Hba.Ride.Domain.ValueObjects;

namespace Hba.Ride.Domain.Rides.Events;

/// <summary>
/// Socle des événements de course.
/// </summary>
///
/// <remarks>
/// ILS PARTENT TOUS DANS <c>hba.ride.events.v1</c>, un seul topic, le type
/// précis voyageant dans l'enveloppe. C'est la convention du dépôt — un flux par
/// service — confirmée au point 35 contre la proposition d'un topic par
/// événement.
/// </remarks>
public abstract record RideDomainEvent(Guid RideId, Actor Actor, DateTimeOffset OccurredAt)
    : DomainEvent(Actor, OccurredAt);

public sealed record RideRequested(
    Guid RideId,
    string CustomerId,
    VehicleType VehicleType,
    Actor Actor,
    DateTimeOffset OccurredAt) : RideDomainEvent(RideId, Actor, OccurredAt);

/// <summary>
/// La recherche commence.
///
/// C'EST CET ÉVÉNEMENT QUE DISPATCH ÉCOUTE, et non RideRequested : entre les
/// deux il y a le paiement. Déclencher la recherche sur la demande enverrait des
/// chauffeurs vers une course que personne n'a payée.
/// </summary>
public sealed record RideDriverSearchStarted(
    Guid RideId,
    Actor Actor,
    DateTimeOffset OccurredAt) : RideDomainEvent(RideId, Actor, OccurredAt);

public sealed record RideDriverAssigned(
    Guid RideId,
    string DriverId,
    Actor Actor,
    DateTimeOffset OccurredAt) : RideDomainEvent(RideId, Actor, OccurredAt);

public sealed record RideDriverArriving(
    Guid RideId,
    string DriverId,
    Actor Actor,
    DateTimeOffset OccurredAt) : RideDomainEvent(RideId, Actor, OccurredAt);

public sealed record RideDriverArrived(
    Guid RideId,
    string DriverId,
    Actor Actor,
    DateTimeOffset OccurredAt) : RideDomainEvent(RideId, Actor, OccurredAt);

public sealed record RideStarted(
    Guid RideId,
    string DriverId,
    Actor Actor,
    DateTimeOffset OccurredAt) : RideDomainEvent(RideId, Actor, OccurredAt);

public sealed record RideCompleted(
    Guid RideId,
    string DriverId,
    MoneyXof FinalPrice,
    Actor Actor,
    DateTimeOffset OccurredAt) : RideDomainEvent(RideId, Actor, OccurredAt);

public sealed record RideCancelled(
    Guid RideId,
    string? Reason,

    /// <summary>
    /// Retenue due au chauffeur, en francs CFA. ZÉRO QUAND AUCUN CHAUFFEUR
    /// N'ÉTAIT AFFECTÉ — il n'y a alors aucun déplacement à dédommager.
    ///
    /// ELLE VOYAGE DANS L'ÉVÉNEMENT PLUTÔT QUE D'ÊTRE RELUE : Payment n'a pas le
    /// droit de lire la base de Ride, et n'a aucune grille tarifaire.
    /// </summary>
    long CancellationFeeXof,

    Actor Actor,
    DateTimeOffset OccurredAt) : RideDomainEvent(RideId, Actor, OccurredAt);

public sealed record RideNoDriverAvailable(
    Guid RideId,
    Actor Actor,
    DateTimeOffset OccurredAt) : RideDomainEvent(RideId, Actor, OccurredAt);

/// <summary>
/// Le paiement a échoué ou expiré.
/// </summary>
///
/// <remarks>
/// IL NE SORT PAS DU SERVICE, et c'est la même raison que pour RideRequested :
/// aucun autre service n'a affaire à une course impayée. Dispatch n'a jamais été
/// prévenu — la recherche n'était pas ouverte —, Billing n'a rien à facturer, et
/// le passager l'apprend par son application. Le jour où Notification devra lui
/// envoyer un message, ce fait prendra un champ dans le contrat.
/// </remarks>
public sealed record RidePaymentFailed(
    Guid RideId,
    string CustomerId,
    string? Reason,
    Actor Actor,
    DateTimeOffset OccurredAt) : RideDomainEvent(RideId, Actor, OccurredAt);
