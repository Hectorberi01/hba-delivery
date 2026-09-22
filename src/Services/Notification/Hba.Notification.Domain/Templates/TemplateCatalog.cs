using Hba.BuildingBlocks.Domain;
using Hba.Notification.Domain.Messages;

namespace Hba.Notification.Domain.Templates;

/// <summary>
/// Les modèles connus du système. Un identifiant inconnu est refusé : un
/// service qui demande l'envoi d'un modèle inexistant a un bug, et laisser
/// passer un message vide serait pire que de le signaler.
/// </summary>
public static class TemplateCatalog
{
    /// <summary>
    /// 160 caractères, et les textes sont volontairement SANS ACCENT.
    /// Un SMS s'encode en GSM-7 tant qu'il ne contient que des caractères de
    /// cet alphabet ; un seul « é » le fait basculer en UCS-2, qui ne tient
    /// plus que 70 caractères par segment. Un message de 120 caractères passe
    /// ainsi d'un SMS à deux, facturés deux fois, sur chaque envoi.
    /// </summary>
    private const int SmsMaxLength = 160;

    /// <summary>Code de connexion, envoyé par Identity.</summary>
    public const string OtpLogin = "otp_login";

    /// <summary>Code de remise, envoyé au destinataire d'un colis.</summary>
    public const string DeliveryOtp = "delivery_otp";

    /// <summary>Un livreur a accepté la course.</summary>
    public const string DeliveryAssigned = "delivery_assigned";

    /// <summary>Le colis a été remis.</summary>
    public const string DeliveryCompleted = "delivery_completed";

    private static readonly Dictionary<string, MessageTemplate> All = new(StringComparer.Ordinal)
    {
        [OtpLogin] = new(
            OtpLogin,
            NotificationChannel.Sms,
            "HBA : votre code de connexion est {code}. Il expire dans {minutes} minutes. Ne le communiquez a personne.",
            SmsMaxLength),

        [DeliveryOtp] = new(
            DeliveryOtp,
            NotificationChannel.Sms,
            "HBA : un colis arrive pour vous (reference {reference}). Code de remise : {code}. Donnez-le au livreur a la remise.",
            SmsMaxLength),

        [DeliveryAssigned] = new(
            DeliveryAssigned,
            NotificationChannel.Sms,
            "HBA : {driver} prend en charge votre livraison {reference}. Vous pouvez l'appeler au {phone}.",
            SmsMaxLength),

        [DeliveryCompleted] = new(
            DeliveryCompleted,
            NotificationChannel.Sms,
            "HBA : votre livraison {reference} a ete remise. Merci.",
            SmsMaxLength),
    };

    public static MessageTemplate Get(string templateId)
        => All.TryGetValue(templateId ?? string.Empty, out var template)
            ? template
            : throw new NotFoundException("Modèle de message", templateId ?? string.Empty);

    public static bool Exists(string templateId) => All.ContainsKey(templateId ?? string.Empty);

    public static IReadOnlyCollection<MessageTemplate> Known => All.Values;
}
