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
