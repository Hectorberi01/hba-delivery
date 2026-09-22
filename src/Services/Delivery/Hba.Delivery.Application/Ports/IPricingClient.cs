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
    /// expiré ou déjà consommé.
    /// </summary>
    Task<PricingSnapshot> ConsumeQuoteAsync(string quoteId, Guid deliveryId, CancellationToken cancellationToken);
}
