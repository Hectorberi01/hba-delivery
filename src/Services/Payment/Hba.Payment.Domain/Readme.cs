namespace Hba.Payment.Domain;

/// <summary>
/// Repere du domaine Payment.
///
/// L'agregat est <see cref="Payments.PaymentIntent"/> : une intention de
/// paiement par livraison, creee en attente, menee a son terme par le webhook
/// verifie du fournisseur. LE FOURNISSEUR N'APPARAIT PAS DANS CE PROJET :
/// FedaPay vit derriere un port de la couche Application, et le domaine ne
/// connait que des references opaques.
///
/// Le remboursement n'est pas encore modelise : voir l'ADR 0017.
/// </summary>
internal static class DomainPlaceholder
{
    public const string ServiceName = "payment";
}
