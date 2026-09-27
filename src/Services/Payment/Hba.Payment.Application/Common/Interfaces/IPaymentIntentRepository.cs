using Hba.Payment.Domain.Payments;

namespace Hba.Payment.Application.Common.Interfaces;

public interface IPaymentIntentRepository
{
    Task<PaymentIntent?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Lecture du chemin webhook : on n'a que la reference du fournisseur.</summary>
    Task<PaymentIntent?> FindByProviderReferenceAsync(string providerReference, CancellationToken cancellationToken);

    /// <summary>
    /// Une livraison n'a qu'une intention vivante. Sert a ne pas en ouvrir une
    /// seconde quand l'appelant rejoue sans cle d'idempotence.
    /// </summary>
    Task<PaymentIntent?> FindActiveByDeliveryAsync(Guid deliveryId, CancellationToken cancellationToken);

    void Add(PaymentIntent intent);
}
