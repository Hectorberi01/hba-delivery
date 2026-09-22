namespace Hba.Delivery.Domain.Deliveries;

/// <summary>
/// Origine de la livraison. Détermine qui est le donneur d'ordre et par où
/// passent les notifications de sortie (webhook partenaire ou Kafka interne).
/// </summary>
public enum DeliverySource
{
    /// <summary>App client Flutter.</summary>
    ClientApp = 1,

    /// <summary>Marketplace HBA Express, via le contrat partenaire.</summary>
    HbaExpress = 2,

    /// <summary>HBA Food, via le contrat partenaire.</summary>
    HbaFood = 3,

    /// <summary>Plateforme externe, via la Partner API publique.</summary>
    PartnerApi = 4,

    /// <summary>Portail commerçant Next.js.</summary>
    MerchantPortal = 5,
}
