using Hba.BuildingBlocks.Domain;
using Hba.Delivery.Domain.ValueObjects;

namespace Hba.Delivery.Domain.Deliveries;

public enum VehicleType
{
    Motorcycle = 1,
    Car = 2,
    Van = 3,
}

/// <summary>
/// Copie locale du strict nécessaire sur le livreur affecté : nom, véhicule,
/// téléphone. Rien d'autre ne doit remonter ici — le client et le commerçant ne
/// voient pas plus, et Delivery n'a pas à connaître le profil complet.
/// </summary>
public sealed class AssignedDriver : ValueObject
{
    private AssignedDriver(string driverId, string displayName, string phone, VehicleType vehicleType, string vehiclePlate)
    {
        DriverId = driverId;
        DisplayName = displayName;
        Phone = phone;
        VehicleType = vehicleType;
        VehiclePlate = vehiclePlate;
    }

    public string DriverId { get; }

    public string DisplayName { get; }

    public string Phone { get; }

    public VehicleType VehicleType { get; }

    public string VehiclePlate { get; }

    public static AssignedDriver Create(
        string driverId,
        string displayName,
        string phone,
        VehicleType vehicleType,
        string vehiclePlate)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(driverId);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

        if (!PhoneNumber.IsValid(phone))
        {
            throw new DomainException("INVALID_DRIVER_PHONE", "Le livreur affecté doit être joignable.");
        }

        return new AssignedDriver(
            driverId,
            displayName.Trim(),
            PhoneNumber.Normalize(phone),
            vehicleType,
            vehiclePlate?.Trim() ?? string.Empty);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return DriverId;
        yield return DisplayName;
        yield return Phone;
        yield return VehicleType;
        yield return VehiclePlate;
    }
}
