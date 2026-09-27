using Hba.BuildingBlocks.Domain;
using Hba.Driver.Domain.Exceptions;

namespace Hba.Driver.Domain.Drivers;

/// <summary>
/// Vehicule declare par le livreur. La capacite sert au dispatch : un colis de
/// vingt kilos ne part pas sur une moto.
/// </summary>
public sealed class Vehicle : ValueObject
{
    private Vehicle(VehicleType type, string plate, int capacityGrams)
    {
        Type = type;
        Plate = plate;
        CapacityGrams = capacityGrams;
    }

    public VehicleType Type { get; }

    /// <summary>Immatriculation. Vide tant que le livreur ne l'a pas declaree.</summary>
    public string Plate { get; }

    public int CapacityGrams { get; }

    /// <summary>
    /// Vehicule par defaut a l'inscription : une moto, sans immatriculation ni
    /// capacite declaree.
    ///
    /// LE ZEMIDJAN EST LE MODE PAR DEFAUT A COTONOU, comme pour la grille
    /// tarifaire. Un profil qui naitrait sans vehicule ne pourrait recevoir
    /// aucune offre, et le livreur ne comprendrait pas pourquoi.
    /// </summary>
    public static Vehicle Unknown => new(VehicleType.Motorcycle, string.Empty, 0);

    public static Vehicle Create(VehicleType type, string? plate, int capacityGrams)
    {
        if (capacityGrams < 0)
        {
            throw new DomainException(
                DriverErrorCodes.InvalidVehicleCapacity,
                "La capacite d'un vehicule ne peut pas etre negative.");
        }

        return new Vehicle(type, plate?.Trim() ?? string.Empty, capacityGrams);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Type;
        yield return Plate;
        yield return CapacityGrams;
    }
}
