namespace Hba.Pricing.Domain.Tariffs;

/// <summary>
/// Reprend les valeurs de hba.common.v1.VehicleType. Le domaine ne dépend pas
/// des contrats générés ; la correspondance est faite dans la couche Api.
/// </summary>
public enum VehicleType
{
    Motorcycle = 1,
    Car = 2,
    Van = 3,
}
