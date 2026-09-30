namespace Hba.Billing.Domain.Exceptions;

/// <summary>
/// Codes métier stables du service Billing. Ce sont EUX que les applications
/// lisent, pas les messages : le message est en français et peut changer, le
/// code est un contrat.
/// </summary>
public static class BillingErrorCodes
{
    /// <summary>
    /// Le compte ne peut pas porter ce débit.
    /// </summary>
    ///
    /// <remarks>
    /// UN SEUL CODE POUR LES DEUX MODES, ET C'EST VOULU. Le prépayé sans solde
    /// et le postpayé au plafond sont le même refus — la règle est la même —
    /// et c'est la MARCHE À SUIVRE qui diffère : recharger, ou régler la
    /// facture en retard. L'application la déduit du mode du compte ; deux
    /// codes obligeraient chaque appelant à connaître cette distinction.
    /// </remarks>
    public const string InsufficientBalance = "INSUFFICIENT_BALANCE";

    public const string AccountSuspended = "ACCOUNT_SUSPENDED";

    public const string InvalidAmount = "INVALID_AMOUNT";

    public const string MissingReference = "MISSING_REFERENCE";

    public const string MissingIdempotencyKey = "MISSING_IDEMPOTENCY_KEY";

    /// <summary>Un plafond de crédit ne se donne pas à un compte prépayé.</summary>
    public const string CreditLimitOnPrepaid = "CREDIT_LIMIT_ON_PREPAID";

    public const string InvalidCreditLimit = "INVALID_CREDIT_LIMIT";

    /// <summary>La clé donnée désigne un mouvement d'un autre compte.</summary>
    public const string MovementNotOnThisAccount = "MOVEMENT_NOT_ON_THIS_ACCOUNT";

    /// <summary>On a demandé d'annuler un mouvement qui n'est pas un débit.</summary>
    public const string NotADebit = "NOT_A_DEBIT";

    /// <summary>Aucun débit ne porte cette clé : il n'y a rien à annuler.</summary>
    public const string DebitNotFound = "DEBIT_NOT_FOUND";
}
