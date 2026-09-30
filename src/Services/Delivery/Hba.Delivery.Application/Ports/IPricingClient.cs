using Hba.Delivery.Domain.ValueObjects;

namespace Hba.Delivery.Application.Ports;

/// <summary>
/// Accès au service Pricing. Delivery ne calcule jamais un prix : il consomme un
/// devis et en fige le contenu.
/// </summary>
public interface IPricingClient
{
    /// <summary>
    /// Consomme le devis et renvoie le snapshot à figer. Échoue si le devis est
    /// expiré, déjà consommé, ou S'IL A ETE ETABLI POUR UN AUTRE TRAJET.
    /// </summary>
    ///
    /// <remarks>
    /// LE TRAJET EST PASSE A PRICING, ET CE N'EST PAS UNE FORMALITE. Delivery
    /// crée la course avec les points de la commande ; Pricing seul sait quel
    /// trajet il a chiffré. Tant qu'on ne lui donnait pas les deux, un devis de
    /// 300 m payait une course de 20 km. Le contrôle appartient à Pricing :
    /// demander à Delivery de vérifier son propre prix n'aurait aucune valeur.
    /// </remarks>
    Task<PricingSnapshot> ConsumeQuoteAsync(
        string quoteId,
        Guid deliveryId,
        GeoPoint pickup,
        GeoPoint dropoff,
        CancellationToken cancellationToken);
}
