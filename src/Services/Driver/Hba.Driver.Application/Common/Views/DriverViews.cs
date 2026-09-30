using Hba.Driver.Application.Common.Interfaces;
using Hba.Driver.Domain.Drivers;

namespace Hba.Driver.Application.Common.Views;

public sealed record DriverView(
    Guid Id,
    string DisplayName,
    string Phone,
    VehicleType VehicleType,
    string VehiclePlate,
    int VehicleCapacityGrams,
    VerificationStatus VerificationStatus,
    OperationalStatus OperationalStatus,
    string StatusReason,
    DateTimeOffset RegisteredAt,
    DateTimeOffset? VerifiedAt)
{
    public static DriverView From(DriverAggregate driver)
    {
        ArgumentNullException.ThrowIfNull(driver);

        return new DriverView(
            driver.Id,
            driver.DisplayName,
            driver.Phone,
            driver.Vehicle.Type,
            driver.Vehicle.Plate,
            driver.Vehicle.CapacityGrams,
            driver.VerificationStatus,
            driver.OperationalStatus,
            driver.StatusReason ?? string.Empty,
            driver.RegisteredAt,
            driver.VerifiedAt);
    }
}

/// <summary>Une page de l'annuaire, et le total du filtre.</summary>
public sealed record DriverPageView(IReadOnlyList<DriverView> Drivers, int Total);

/// <summary>
/// Ce que le client et le commercant ont le droit de voir pendant la mission :
/// nom, vehicule, telephone. RIEN DE PLUS NE DOIT TRANSITER — ni le dossier,
/// ni l'etat operationnel, ni la position historique.
/// </summary>
public sealed record DriverPublicProfileView(
    Guid DriverId,
    string DisplayName,
    string Phone,
    VehicleType VehicleType,
    string VehiclePlate)
{
    public static DriverPublicProfileView From(DriverAggregate driver)
    {
        ArgumentNullException.ThrowIfNull(driver);

        return new DriverPublicProfileView(
            driver.Id,
            driver.DisplayName,
            driver.Phone,
            driver.Vehicle.Type,
            driver.Vehicle.Plate);
    }
}

public sealed record NearbyDriverView(
    Guid DriverId,
    int DistanceMeters,
    double Latitude,
    double Longitude,

    /// <summary>
    /// Le vehicule, pour la carte du client. Point 24, revise le 30 septembre
    /// 2026 : c'est le seul attribut du livreur qui sorte avant l'affectation,
    /// et il sort SANS son identifiant.
    /// </summary>
    VehicleType VehicleType);

/// <summary>
/// Reponse a « puis-je proposer une course a CE livreur, depuis CE point ».
///
/// ELLE PORTE LE MOTIF DU REFUS, ET C'EST TOUT L'INTERET. Un booleen seul
/// laisserait l'exploitation devant un bouton grise sans explication ; or les
/// quatre raisons appellent quatre gestes differents — valider un dossier,
/// attendre la fin d'une course, rappeler quelqu'un, ou ne rien faire parce
/// que le telephone est eteint.
/// </summary>
public sealed record DriverAvailabilityView(
    Guid DriverId,
    string DisplayName,
    bool Offerable,
    string? Reason,
    VerificationStatus VerificationStatus,
    OperationalStatus OperationalStatus,
    MeasuredPosition? Position);
