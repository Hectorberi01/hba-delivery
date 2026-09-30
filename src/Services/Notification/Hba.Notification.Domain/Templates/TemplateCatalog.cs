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

    /// <summary>
    /// Le récapitulatif de la course, par courriel, une fois le colis remis.
    /// </summary>
    ///
    /// <remarks>
    /// UN MODELE A PART, ET NON UN CANAL DE PLUS SUR « delivery_completed ».
    /// Les deux messages ne disent pas la même chose : le SMS annonce
    /// l'événement, et il est court parce qu'il se paie au segment ; celui-ci
    /// se garde — il porte les deux adresses, le prix et la date, et personne
    /// ne relit un SMS pour retrouver une dépense.
    ///
    /// Les réunir sous un même identifiant aurait surtout fait du courriel un
    /// REPLI du SMS dans la chaîne du catalogue : le premier canal qui aboutit
    /// gagne, et les suivants ne partent pas. Or ces deux-là doivent partir
    /// tous les deux.
    ///
    /// CE N'EST PAS UNE FACTURE, et le texte le dit lui-même. Une facture au
    /// Bénin suppose une numérotation continue, l'IFU et le régime de TVA de
    /// HBA : rien de tout cela n'existe dans le dépôt, et l'inventer
    /// produirait un document faux que des clients garderaient. Point 29.
    /// </remarks>
    public const string DeliveryReceipt = "delivery_receipt";

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

        // COURRIEL SEULEMENT, ET AUCUN REPLI. Un reçu qui basculerait en SMS
        // arriverait tronqué et coûterait trois segments pour dire moins que
        // rien. Sans adresse connue, il ne part pas — et c'est consigné
        // « ignoré », ce qui n'est pas un échec.
        //
        // LES ACCENTS SONT ICI, contrairement aux modèles SMS juste au-dessus :
        // la contrainte GSM-7 qui les interdit là ne vaut pas pour un courriel,
        // qui s'écrit en UTF-8. Les omettre serait une faute sans excuse.
        [DeliveryReceipt] = new(
            DeliveryReceipt,
            [NotificationChannel.Email],
            email: new(
                DeliveryReceipt,
                "Votre course {reference} — HBA Delivery",
                """
                Bonjour,

                Votre colis a été remis le {date}.

                Référence    : {reference}
                Départ       : {depart}
                Arrivée      : {arrivee}
                Montant payé : {montant}

                Ce message est un reçu : il confirme que la course a eu lieu
                et qu'elle a été réglée. Ce n'est pas une facture.

                Une question sur cette course ? Répondez à ce message en
                gardant la référence dans l'objet.

                HBA Delivery
                """)),
    };

    public static TemplateBinding Get(string templateId)
        => All.TryGetValue(templateId ?? string.Empty, out var binding)
            ? binding
            : throw new NotFoundException("Modèle de message", templateId ?? string.Empty);

    public static bool Exists(string templateId) => All.ContainsKey(templateId ?? string.Empty);

    public static IReadOnlyCollection<TemplateBinding> Known => All.Values;
}
