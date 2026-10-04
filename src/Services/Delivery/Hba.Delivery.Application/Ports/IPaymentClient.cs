using Hba.Delivery.Domain.ValueObjects;

namespace Hba.Delivery.Application.Ports;

/// <summary>
/// Accès au service Payment. Le fournisseur (FedaPay) reste derrière : Delivery
/// ne le connaît pas.
/// </summary>
public interface IPaymentClient
{
    /// <summary>
    /// Ouvre le paiement d'une livraison et rend la page où envoyer le payeur.
    /// </summary>
    ///
    /// <remarks>
    /// LE NUMÉRO EST FACULTATIF : vide, Payment reprend celui du jeton. Il ne
    /// fait que préremplir la page du fournisseur, où le client peut en saisir
    /// un autre — et c'est celui-là qui sera débité. Le champ existe parce qu'on
    /// s'inscrit avec le téléphone qu'on porte et qu'on paie avec celui qui a de
    /// l'argent dessus.
    /// </remarks>
    Task<PaymentIntentResult> CreateIntentAsync(
        string idempotencyKey,
        Guid deliveryId,
        string payerId,
        string payerPhone,
        MoneyXof amount,
        CancellationToken cancellationToken);
}

/// <summary>
/// Ce que Payment rend : l'identifiant de l'intention et la page à ouvrir.
/// </summary>
///
/// <remarks>
/// L'ADRESSE EST REVENUE LE 3 OCTOBRE 2026, le jour même où elle avait disparu :
/// la poussée directe sur le téléphone du payeur est refusée par le fournisseur
/// sur notre compte, et la page hébergée a repris sa place.
/// </remarks>
public sealed record PaymentIntentResult(string PaymentIntentId, string RedirectUrl);
