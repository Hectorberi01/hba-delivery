using Hba.BuildingBlocks.Domain;
using Hba.Notification.Domain.Messages;

namespace Hba.Notification.Domain.Templates;

/// <summary>
/// Ce qu'un message logique devient sur chaque canal.
///
/// Un même identifiant — « otp_login » — n'a pas la même forme selon qu'il part
/// en SMS, où l'on écrit le texte, ou en WhatsApp, où l'on nomme un modèle
/// approuvé. La liaison porte les deux, et l'ordre des canaux dit lequel
/// essayer d'abord.
/// </summary>
public sealed class TemplateBinding
{
    internal TemplateBinding(
        string id,
        IReadOnlyList<NotificationChannel> channels,
        MessageTemplate? sms = null,
        WhatsAppTemplate? whatsApp = null)
    {
        Id = id;
        Channels = channels;
        Sms = sms;
        WhatsApp = whatsApp;
    }

    public string Id { get; }

    /// <summary>
    /// Canaux par ordre de préférence. Le premier qui est configuré et qui
    /// aboutit gagne ; les suivants sont des replis.
    /// </summary>
    public IReadOnlyList<NotificationChannel> Channels { get; }

    public MessageTemplate? Sms { get; }

    public WhatsAppTemplate? WhatsApp { get; }

    public bool Supports(NotificationChannel channel) => channel switch
    {
        NotificationChannel.Sms => Sms is not null,
        NotificationChannel.WhatsApp => WhatsApp is not null,
        _ => false,
    };

    public MessageTemplate RequireSms() =>
        Sms ?? throw new DomainException(
            "CHANNEL_UNSUPPORTED",
            $"Le message {Id} n'a pas de modèle SMS.");

    public WhatsAppTemplate RequireWhatsApp() =>
        WhatsApp ?? throw new DomainException(
            "CHANNEL_UNSUPPORTED",
            $"Le message {Id} n'a pas de modèle WhatsApp.");
}
