namespace Hba.Delivery.Domain.Deliveries;

/// <summary>
/// États d'une livraison. Les valeurs numériques sont persistées : ne jamais les
/// réutiliser ni les décaler.
/// </summary>
public enum DeliveryStatus
{
    /// <summary>Créée, en attente du webhook FedaPay.</summary>
    PendingPayment = 1,

    /// <summary>Paiement échoué ou expiré. Terminal.</summary>
    PaymentFailed = 2,

    /// <summary>Paiement confirmé. Aucune recherche de livreur avant cet état.</summary>
    Paid = 3,

    /// <summary>Le moteur de dispatch envoie des offres par vagues.</summary>
    SearchingDriver = 4,

    /// <summary>Toutes les vagues ont échoué. Terminal, déclenche un remboursement.</summary>
    NoDriverFound = 5,

    DriverAssigned = 6,

    DriverAtPickup = 7,

    PickedUp = 8,

    /// <summary>Terminal. Seul le livreur y accède, et seulement avec un OTP valide.</summary>
    Delivered = 9,

    Cancelled = 10,

    Failed = 11,
}
