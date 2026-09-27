namespace Hba.Payment.Domain.Exceptions;

/// <summary>
/// Codes metier stables du service Payment. Ce sont EUX que les applications
/// lisent, pas les messages : le message est en francais et peut changer, le
/// code est un contrat.
/// </summary>
public static class PaymentErrorCodes
{
    public const string InvalidAmount = "INVALID_AMOUNT";

    public const string MissingDeliveryId = "MISSING_DELIVERY_ID";

    public const string MissingPayer = "MISSING_PAYER";

    public const string CheckoutAlreadyAttached = "CHECKOUT_ALREADY_ATTACHED";

    public const string PaymentAlreadySucceeded = "PAYMENT_ALREADY_SUCCEEDED";

    public const string ProviderUnavailable = "PROVIDER_UNAVAILABLE";

    public const string ProviderRejected = "PROVIDER_REJECTED";

    public const string InvalidWebhookSignature = "INVALID_WEBHOOK_SIGNATURE";

    public const string RefundNotImplemented = "REFUND_NOT_IMPLEMENTED";

    // --- Versements aux livreurs ---

    /// <summary>Une demande est deja en cours pour ce livreur.</summary>
    public const string PayoutAlreadyPending = "PAYOUT_ALREADY_PENDING";

    /// <summary>Le montant demande depasse ce qui est disponible.</summary>
    public const string PayoutExceedsBalance = "PAYOUT_EXCEEDS_BALANCE";

    /// <summary>Le montant demande est sous le minimum configure.</summary>
    public const string PayoutBelowMinimum = "PAYOUT_BELOW_MINIMUM";

    public const string MissingRejectionReason = "MISSING_REJECTION_REASON";

    public const string MissingPaymentReference = "MISSING_PAYMENT_REFERENCE";
}
