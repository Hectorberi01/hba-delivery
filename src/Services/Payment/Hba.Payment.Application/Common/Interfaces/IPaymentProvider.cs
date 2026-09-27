using Hba.Payment.Domain.Payments;
using Hba.Payment.Domain.ValueObjects;

namespace Hba.Payment.Application.Common.Interfaces;

/// <summary>
/// Transaction ouverte chez le fournisseur : sa reference, et l'adresse a
/// laquelle envoyer le payeur.
/// </summary>
public sealed record ProviderCheckout(string Reference, string RedirectUrl);

/// <summary>
/// Etat d'une transaction relu chez le fournisseur.
/// <paramref name="Raw"/> conserve le libelle d'origine pour les journaux et le
/// rapprochement : un statut que nous ne connaissons pas encore ne doit pas
/// disparaitre dans un « inconnu » sans trace.
/// </summary>
public sealed record ProviderPayment(PaymentStatus Status, string Raw, string? FailureReason);

/// <summary>
/// Fournisseur de paiement.
///
/// TOUTE LA CONNAISSANCE DE FEDAPAY TIENT DERRIERE CETTE INTERFACE. Le contrat
/// le dit explicitement : « Payment encapsule FedaPay derriere IPaymentProvider.
/// Aucun autre service ne connait le fournisseur. » Changer d'agregateur, c'est
/// ecrire une autre implementation, pas toucher au domaine.
/// </summary>
public interface IPaymentProvider
{
    /// <summary>Nom du fournisseur, pour les journaux et le rapport de demarrage.</summary>
    string Name { get; }

    Task<ProviderCheckout> OpenCheckoutAsync(
        Guid paymentIntentId,
        Guid deliveryId,
        MoneyXof amount,
        string payerPhone,
        string? returnUrl,
        CancellationToken cancellationToken);

    /// <summary>
    /// Relit l'etat de la transaction A LA SOURCE.
    ///
    /// C'est volontairement une requete sortante et non une lecture du corps du
    /// webhook : le webhook dit QU'IL S'EST PASSE QUELQUE CHOSE, il ne dit pas
    /// ce qu'il faut croire. Sa signature prouve l'emetteur, pas la fraicheur
    /// de son contenu ; et le fournisseur lui-meme recommande de verifier le
    /// statut par l'API.
    /// </summary>
    Task<ProviderPayment> GetPaymentAsync(string providerReference, CancellationToken cancellationToken);
}

/// <summary>
/// Verification de l'authenticite d'un webhook. L'implementation est propre au
/// fournisseur ; le point d'entree HTTP ne connait que cette interface.
/// </summary>
public interface IWebhookVerifier
{
    /// <summary>
    /// Rend la reference de transaction portee par la notification, ou null si
    /// la signature ne tient pas, si l'horodatage est hors tolerance, ou si le
    /// corps ne nomme aucune transaction.
    /// </summary>
    string? ReadTransactionReference(string rawBody, string? signatureHeader);
}
