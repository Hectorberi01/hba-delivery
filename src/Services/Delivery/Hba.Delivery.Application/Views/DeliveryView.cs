using Hba.Delivery.Application.Ports;
using Hba.Delivery.Domain.Deliveries;

namespace Hba.Delivery.Application.Views;

/// <summary>
/// Projection d'une livraison APRÈS application de la matrice de visibilité.
/// Les champs absents sont nuls : il n'existe pas de version « complète » de ce
/// DTO qui circulerait par erreur.
/// </summary>
public sealed record DeliveryView
{
    public required Guid Id { get; init; }

    public required string Reference { get; init; }

    public required DeliveryStatus Status { get; init; }

    public required DeliverySource Source { get; init; }

    public string? PartnerId { get; init; }

    public string? ExternalOrderId { get; init; }

    public string? CustomerId { get; init; }

    public string? MerchantId { get; init; }

    public string? PickupPointId { get; init; }

    public required LocationView Pickup { get; init; }

    /// <summary>
    /// Nul tant que le livreur n'a pas accepté : avant acceptation, il ne voit
    /// que le repère de collecte.
    /// </summary>
    public LocationView? Dropoff { get; init; }

    public string? RecipientName { get; init; }

    public string? RecipientPhone { get; init; }

    /// <summary>Détail tarifaire complet. Nul pour le livreur.</summary>
    public PricingView? Pricing { get; init; }

    /// <summary>Rémunération du livreur, seule part qu'il peut voir.</summary>
    public long? DriverEarningXof { get; init; }

    public AssignedDriverView? Driver { get; init; }

    /// <summary>
    /// Code de remise. Renseigné pour le seul client donneur d'ordre. Jamais
    /// pour le livreur, le commerçant, le partenaire ni l'administrateur.
    /// </summary>
    public string? DeliveryOtp { get; init; }

    public string? PackageDescription { get; init; }

    public int PackageWeightGrams { get; init; }

    public string? ClosureReason { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? PaidAt { get; init; }

    public DateTimeOffset? AssignedAt { get; init; }

    public DateTimeOffset? PickedUpAt { get; init; }

    public DateTimeOffset? CompletedAt { get; init; }

    /// <summary>
    /// Instant où le remboursement a été constaté. Nul quand il n'y en a pas.
    /// </summary>
    ///
    /// <remarks>
    /// RÉSERVÉ AU DONNEUR D'ORDRE ET AU BACK-OFFICE. Un livreur n'a rien à savoir
    /// de ce qui s'est passé entre le client et HBA après la course.
    /// </remarks>
    public DateTimeOffset? RefundedAt { get; init; }

    /// <summary>Vrai quand le remboursement constaté est partiel.</summary>
    public bool RefundPartial { get; init; }

    /// <summary>
    /// Ce qui serait retenu si le donneur d'ordre annulait MAINTENANT. Zéro tant
    /// qu'aucun livreur n'est affecté, et zéro pour toujours si la grille de la
    /// zone ne prévoit pas de retenue.
    /// </summary>
    ///
    /// <remarks>
    /// ELLE EST ICI POUR ÊTRE AFFICHÉE AVANT LA CONFIRMATION. L'écran promettait
    /// que « les conditions de remboursement vous seront confirmées par le
    /// service » — une promesse que personne ne tenait, faute de politique
    /// d'annulation. Elle est tranchée depuis le 2 octobre 2026, et ce champ est
    /// ce qui permet enfin de dire un chiffre.
    ///
    /// LE DONNEUR D'ORDRE ET LE BACK-OFFICE SEULEMENT. Un livreur n'a pas à voir
    /// ce que le client paierait pour annuler ; ce qu'il touche, lui, apparaît sur
    /// son relevé une fois l'annulation faite.
    /// </remarks>
    public long? CancellationFeeDueXof { get; init; }

    /// <summary>
    /// L'itinéraire qui reste au livreur, et la durée qui en découle. Nul quand
    /// on ne sait pas.
    /// </summary>
    ///
    /// <remarks>
    /// LA CIBLE CHANGE AVEC L'ÉTAT : le point de collecte avant l'enlèvement, le
    /// destinataire après. Un seul champ, deux cibles — l'appelant n'a pas à
    /// choisir, le service sait où en est la mission.
    ///
    /// NUL N'EST PAS UN CAS RARE : aucun livreur affecté, aucune position encore
    /// remontée, Routing injoignable, mission close. L'appelant DOIT traiter ce
    /// cas.
    ///
    /// IL N'EST JAMAIS CONSERVÉ (point 43) : recalculé à la lecture, oublié
    /// aussitôt. Une suite d'itinéraires gardés serait la piste que le point 37
    /// refuse.
    ///
    /// LE DONNEUR D'ORDRE ET LE BACK-OFFICE SEULEMENT. Le livreur a sa propre
    /// navigation et n'a que faire de sa propre heure d'arrivée.
    /// </remarks>
    public RouteActive? Itineraire { get; init; }
}

public sealed record LocationView(
    double Latitude,
    double Longitude,
    string Landmark,
    string? Phone,
    string ContactName,
    string? Notes);

public sealed record PricingView(
    long TotalXof,
    long BaseFareXof,
    long DistanceFareXof,
    long SurgeFareXof,
    long DriverEarningXof,
    int DistanceMeters,
    int DurationSeconds,
    string TariffVersion);

/// <summary>Le livreur affecté, réduit à ce que la matrice autorise.</summary>
public sealed record AssignedDriverView(
    string DriverId,
    string DisplayName,
    string? Phone,
    VehicleType VehicleType,
    string VehiclePlate);
