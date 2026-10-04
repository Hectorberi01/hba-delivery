namespace Hba.Payment.Domain.Payments;

/// <summary>
/// Etat d'une intention de paiement. Les valeurs suivent celles du contrat
/// <c>payment_service.proto</c> : elles sont lues par Delivery et par le
/// back-office finance.
/// </summary>
public enum PaymentStatus
{
    Unspecified = 0,
    Pending = 1,
    Succeeded = 2,
    Failed = 3,
    Expired = 4,
    Refunded = 5,
    PartiallyRefunded = 6,
}

public enum PaymentMethod
{
    Unspecified = 0,
    MobileMoney = 1,
    Card = 2,
}

/// <summary>
/// POURQUOI ce paiement a lieu.
/// </summary>
///
/// <remarks>
/// MISSION VAUT ZERO, ET CE N'EST PAS UN HASARD. Toutes les intentions ecrites
/// avant le 3 octobre 2026 reglaient une mission ; en faisant d'elle la valeur
/// par defaut de l'enumere, la colonne ajoutee les decrit correctement sans
/// qu'aucune ligne n'ait a etre reecrite.
///
/// IL NE SE CONFOND PAS AVEC ServiceType, qui dit de quelle MISSION il s'agit.
/// Les deux se croisent : on peut payer une course, puis laisser un pourboire
/// sur cette meme course. Sans cette distinction, la seconde intention passerait
/// pour un rejeu de la premiere et le pourboire ne partirait jamais.
/// </remarks>
public enum PaymentPurpose
{
    /// <summary>Le prix de la mission elle-meme.</summary>
    Mission = 0,

    /// <summary>
    /// Un pourboire, laisse APRES la mission (point 42).
    ///
    /// IL N'EST PAS LIE AU PRIX. Le client choisit son montant depuis le recu ;
    /// rien ne le borne a un pourcentage, et rien ne l'exige.
    /// </summary>
    Tip = 1,
}

/// <summary>
/// Ce qui est paye : une livraison, ou une course de personnes.
/// </summary>
public enum ServiceType
{
    Unspecified = 0,
    Delivery = 1,
    Ride = 2,
}
