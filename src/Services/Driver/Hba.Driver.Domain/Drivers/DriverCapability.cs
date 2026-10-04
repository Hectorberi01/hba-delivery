namespace Hba.Driver.Domain.Drivers;

public enum DriverCapability
{
    Unspecified = 0,

    /// <summary>Le metier d'origine : un colis d'un point a un autre.</summary>
    PackageDelivery = 1,

    /// <summary>Transport de personnes. Ouvert le 1er octobre 2026.</summary>
    PassengerTransport = 2,

    /// <summary>Livraison de repas, pour HBA Food.</summary>
    FoodDelivery = 3,
}
