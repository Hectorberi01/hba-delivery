using Hba.BuildingBlocks.Domain;
using Hba.Payment.Domain.ValueObjects;

namespace Hba.Payment.Domain.Payments.Events;

/// <summary>
/// L'argent est arrive. C'EST LE SEUL FAIT QUI FAIT PASSER UNE LIVRAISON EN
/// PAID : Delivery n'ecoute rien d'autre, et surtout pas le retour de
/// l'application cliente apres la page de paiement.
/// </summary>
public sealed record PaymentIntentSucceeded(
    Guid PaymentIntentId,
    Guid DeliveryId,
    MoneyXof Amount,
    Actor Actor,
    DateTimeOffset OccurredAt) : DomainEvent(Actor, OccurredAt);

public sealed record PaymentIntentFailed(
    Guid PaymentIntentId,
    Guid DeliveryId,
    string Reason,
    Actor Actor,
    DateTimeOffset OccurredAt) : DomainEvent(Actor, OccurredAt);

/// <summary>
/// L'argent a ete rendu. CONSTATE, JAMAIS DECLENCHE PAR NOUS.
/// </summary>
///
/// <remarks>
/// FEDAPAY N'A PAS D'API DE REMBOURSEMENT — verifie le 30 septembre 2026 dans sa
/// documentation : « Refunds are currently only available with MTN Mobile Money
/// and can only be made from the dashboard of your merchant account. » Ce fait
/// ne peut donc pas naitre d'une commande : il nait de la RELECTURE de la
/// transaction, quand le fournisseur la rend « refunded » apres qu'un humain a
/// rembourse depuis le tableau de bord.
///
/// LE MONTANT EST CELUI DE L'INTENTION, ET « Partiel » DIT QUAND IL EST FAUX. Le
/// statut du fournisseur distingue le remboursement total du partiel, mais ne
/// dit PAS combien a ete rendu dans le second cas. On ne l'invente pas : le
/// drapeau porte l'incertitude, et c'est au tableau de bord de donner le
/// montant exact.
/// </remarks>
public sealed record PaymentIntentRefunded(
    Guid PaymentIntentId,
    Guid DeliveryId,
    MoneyXof Amount,
    bool Partiel,
    Actor Actor,
    DateTimeOffset OccurredAt) : DomainEvent(Actor, OccurredAt);
