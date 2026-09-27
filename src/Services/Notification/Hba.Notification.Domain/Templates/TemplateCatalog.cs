using Hba.BuildingBlocks.Domain;
using Hba.Notification.Domain.Messages;

namespace Hba.Notification.Domain.Templates;

/// <summary>
/// Les messages connus du système, et ce qu'ils deviennent sur chaque canal.
/// Un identifiant inconnu est refusé : un service qui demande l'envoi d'un
/// message inexistant a un bug, et laisser passer un message vide serait pire
/// que de le signaler.
/// </summary>
public static class TemplateCatalog
{
    /// <summary>
    /// 160 caractères, et les textes sont volontairement SANS ACCENT.
    /// Un SMS s'encode en GSM-7 tant qu'il ne contient que des caractères de
    /// cet alphabet ; un seul « é » le fait basculer en UCS-2, qui ne tient
    /// plus que 70 caractères par segment.
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

    private static readonly Dictionary<string, TemplateBinding> All = new(StringComparer.Ordinal)
    {
        // WHATSAPP D'ABORD, SMS EN REPLI. Le titulaire du compte donne son
        // consentement à l'inscription, ce qui ouvre WhatsApp ; à environ 2 F
        // le message contre 10 à 25 F le SMS, l'écart paie largement le repli.
        [OtpLogin] = new(
            OtpLogin,
            [NotificationChannel.WhatsApp, NotificationChannel.Sms],
            sms: new(
                OtpLogin,
                NotificationChannel.Sms,
                "HBA : votre code de connexion est {code}. Il expire dans {minutes} minutes. Ne le communiquez a personne.",
                SmsMaxLength),
            whatsApp: new("hba_otp_login", "fr")),

        // SMS SEULEMENT, ET CE N'EST PAS UN OUBLI. Ce code part vers le
        // DESTINATAIRE, qui n'a pas de compte HBA et n'a donc jamais donné
        // d'opt-in. Meta exige ce consentement avant tout message de gabarit :
        // WhatsApp est fermé ici, quel que soit son prix.
        [DeliveryOtp] = new(
            DeliveryOtp,
            [NotificationChannel.Sms],
            sms: new(
                DeliveryOtp,
                NotificationChannel.Sms,
                "HBA : un colis arrive pour vous (reference {reference}). Code de remise : {code}. Donnez-le au livreur a la remise.",
                SmsMaxLength)),

        [DeliveryAssigned] = new(
            DeliveryAssigned,
            [NotificationChannel.Sms],
            sms: new(
                DeliveryAssigned,
                NotificationChannel.Sms,
                "HBA : {driver} prend en charge votre livraison {reference}. Vous pouvez l'appeler au {phone}.",
                SmsMaxLength)),

        [DeliveryCompleted] = new(
            DeliveryCompleted,
            [NotificationChannel.Sms],
            sms: new(
                DeliveryCompleted,
                NotificationChannel.Sms,
                "HBA : votre livraison {reference} a ete remise. Merci.",
                SmsMaxLength)),
    };

    public static TemplateBinding Get(string templateId)
        => All.TryGetValue(templateId ?? string.Empty, out var binding)
            ? binding
            : throw new NotFoundException("Modèle de message", templateId ?? string.Empty);

    public static bool Exists(string templateId) => All.ContainsKey(templateId ?? string.Empty);

    public static IReadOnlyCollection<TemplateBinding> Known => All.Values;
}
