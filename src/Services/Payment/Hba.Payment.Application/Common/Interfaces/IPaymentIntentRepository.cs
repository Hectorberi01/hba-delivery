using Hba.Payment.Domain.Payments;

namespace Hba.Payment.Application.Common.Interfaces;

public interface IPaymentIntentRepository
{
    Task<PaymentIntent?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Lecture du chemin webhook : on n'a que la reference du fournisseur.</summary>
    Task<PaymentIntent?> FindByProviderReferenceAsync(string providerReference, CancellationToken cancellationToken);

    /// <summary>
    /// Une mission n'a qu'une intention vivante. Sert a ne pas en ouvrir une
    /// seconde quand l'appelant rejoue sans cle d'idempotence.
    ///
    /// LES TROIS ARGUMENTS SONT NECESSAIRES. Deux missions de natures differentes
    /// peuvent porter le meme identifiant, et chercher sur l'identifiant seul
    /// rendrait l'intention de l'une pour l'autre — donc une course payee au prix
    /// d'un colis.
    ///
    /// LA RAISON DU PAIEMENT EST ENTREE LE 3 OCTOBRE 2026, avec le pourboire.
    /// Sans elle, un pourboire sur une course deja payee retomberait sur
    /// l'intention de la course : l'appel rendrait un paiement reussi, et le
    /// client croirait avoir laisse un pourboire sans qu'un centime ne bouge.
    /// </summary>
    Task<PaymentIntent?> FindActiveByReferenceAsync(
        ServiceType serviceType,
        Guid referenceId,
        PaymentPurpose purpose,
        CancellationToken cancellationToken);

    void Add(PaymentIntent intent);
}
