namespace Hba.Identity.Domain.Exceptions;

/// <summary>
/// Codes d'erreur métier d'Identity.
///
/// CE NE SONT PAS DES DETAILS INTERNES : chaque code voyage jusqu'aux
/// applications, dans le trailer gRPC « hba-error-code » puis dans le champ
/// « code » de la réponse HTTP des BFF. Le message, lui, est en français et
/// peut changer ; le code, non. Le renommer casse un écran de téléphone déjà
/// installé.
///
/// D'où ce fichier : tant que ces chaînes étaient écrites à la main sur
/// quatorze lignes éparpillées, rien n'empêchait deux orthographes du même
/// code, ni un renommage silencieux.
/// </summary>
public static class IdentityErrorCodes
{
    /// <summary>Code fourni incorrect, expiré ou déjà consommé.</summary>
    public const string InvalidOtp = "INVALID_OTP";

    /// <summary>Aucun défi ne correspond à cet identifiant.</summary>
    public const string OtpNotFound = "OTP_NOT_FOUND";

    /// <summary>Le défi a dépassé sa durée de vie.</summary>
    public const string OtpExpired = "OTP_EXPIRED";

    /// <summary>Trop de demandes pour ce numéro.</summary>
    public const string OtpRateLimited = "OTP_RATE_LIMITED";


    public const string EmailAlreadyUsed = "EMAIL_ALREADY_USED";

    public const string PhoneAlreadyUsed = "PHONE_ALREADY_USED";

    public const string InvalidEmail = "INVALID_EMAIL";

    public const string InvalidPhone = "INVALID_PHONE";

    public const string InvalidId = "INVALID_ID";

    public const string UnknownRole = "UNKNOWN_ROLE";

    public const string InvalidRoles = "INVALID_ROLES";

    /// <summary>Ce compte est déjà rattaché à un livreur.</summary>
    public const string DriverAlreadyLinked = "DRIVER_ALREADY_LINKED";

    public const string InvalidWebhookUrl = "INVALID_WEBHOOK_URL";

    public const string InvalidRateLimit = "INVALID_RATE_LIMIT";

    public const string UnknownSource = "UNKNOWN_SOURCE";

    /// <summary>Une échéance d'effacement déjà passée ne laisse pas annuler.</summary>
    public const string DeletionDateInPast = "DELETION_DATE_IN_PAST";

    /// <summary>On efface un compte qui n'a rien demandé.</summary>
    public const string DeletionNotRequested = "DELETION_NOT_REQUESTED";

    /// <summary>L'échéance n'est pas encore atteinte.</summary>
    public const string DeletionNotDue = "DELETION_NOT_DUE";
}
