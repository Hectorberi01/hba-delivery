using Hba.BuildingBlocks.Domain;

namespace Hba.Driver.Domain.Drivers.Events;

/// <summary>
/// Faits metier de l'agregat Driver. Ils restent internes au service ; la
/// couche Application decide lesquels deviennent des messages Kafka.
/// </summary>
public abstract record DriverDomainEvent(Guid DriverId, Actor Actor, DateTimeOffset OccurredAt)
    : DomainEvent(Actor, OccurredAt);

public sealed record DriverRegistered(
    Guid DriverId,
    string Phone,
    Actor Actor,
    DateTimeOffset OccurredAt) : DriverDomainEvent(DriverId, Actor, OccurredAt);

public sealed record DriverKycReviewed(
    Guid DriverId,
    VerificationStatus Result,
    string Reason,
    string ReviewedBy,
    Actor Actor,
    DateTimeOffset OccurredAt) : DriverDomainEvent(DriverId, Actor, OccurredAt);

/// <summary>
/// Changement d'etat operationnel. IL SORT DU SERVICE parce que le dispatch en
/// depend : un livreur qui passe hors ligne ne doit plus recevoir d'offre, et
/// personne ne peut le deviner en interrogeant sa position.
/// </summary>
public sealed record DriverStatusChanged(
    Guid DriverId,
    OperationalStatus Previous,
    OperationalStatus Current,
    Actor Actor,
    DateTimeOffset OccurredAt) : DriverDomainEvent(DriverId, Actor, OccurredAt);

// ----------------------------------------- Constitution du dossier (ADR 0021) --

/// <summary>
/// Une piece a ete deposee ou remplacee.
///
/// LA CLE Y FIGURE, PAS LE BINAIRE. Ces evenements passent par l'Outbox et
/// finissent dans des journaux : y mettre une piece d'identite reviendrait a
/// la recopier partout ou le journal est lu.
/// </summary>
public sealed record DriverDocumentAttached(
    Guid DriverId,
    DocumentType Type,
    string ObjectKey,
    Actor Actor,
    DateTimeOffset OccurredAt) : DriverDomainEvent(DriverId, Actor, OccurredAt);

public sealed record DriverVehicleDeclared(
    Guid DriverId,
    VehicleType Type,
    string Plate,
    Actor Actor,
    DateTimeOffset OccurredAt) : DriverDomainEvent(DriverId, Actor, OccurredAt);

public sealed record DriverProfilePhotoChanged(
    Guid DriverId,
    string ObjectKey,
    Actor Actor,
    DateTimeOffset OccurredAt) : DriverDomainEvent(DriverId, Actor, OccurredAt);

/// <summary>
/// Le dossier part a l'examen.
///
/// IL NE SORT PAS ENCORE DU SERVICE. Le publieur d'evenements d'integration
/// ignore ce fait : aucun message Kafka ne part, donc rien ne previent ops.
/// En attendant, la console le voit en filtrant l'annuaire sur
/// PENDING_VERIFICATION — elle doit y penser, personne ne l'y pousse.
///
/// Ce qui manque n'est pas le code du message mais la decision : qui est
/// prevenu, par quel canal. Le catalogue de gabarits de Notification n'en a
/// aucun pour cela, et en inventer un placerait une regle metier dans un
/// adaptateur.
/// </summary>
public sealed record DriverApplicationSubmitted(
    Guid DriverId,
    Actor Actor,
    DateTimeOffset OccurredAt) : DriverDomainEvent(DriverId, Actor, OccurredAt);
