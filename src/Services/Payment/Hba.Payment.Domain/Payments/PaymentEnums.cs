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
