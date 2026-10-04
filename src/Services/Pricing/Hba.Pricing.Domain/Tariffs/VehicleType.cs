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
    // aucun devis ne sort pour ce vehicule : le service repond NO_TARIFF.
    //
    // LES MONTANTS NE SONT PAS DANS LE REFERENTIEL. Ils vivent en base, ecrits
    // par admin ou finance (voir PricingAccess) depuis le 1er octobre 2026.
    // Avant cette date la seule source etait la configuration, semee au
    // demarrage pour UN couple (zone, vehicule) : les quatre autres vehicules
    // etaient donc incommandables, sans qu'aucun ecran ne le dise.
    Bicycle = 4,
    Tricycle = 5,
}
