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

    // CHAQUE VALEUR AJOUTEE ICI EST UNE GRILLE TARIFAIRE A CREER, et sans elle
    // aucun devis ne sort pour ce vehicule. Les montants ne sont pas dans le
    // referentiel : ils viennent de la configuration, administres par ops.
    Bicycle = 4,
    Tricycle = 5,
}
