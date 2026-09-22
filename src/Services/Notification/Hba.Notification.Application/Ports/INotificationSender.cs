using Hba.Notification.Domain.Messages;

namespace Hba.Notification.Application.Ports;

public sealed record SendResult(bool Succeeded, string Provider, string? ProviderMessageId, string? Error)
{
    public static SendResult Ok(string provider, string? messageId = null) => new(true, provider, messageId, null);

    public static SendResult Failed(string provider, string error) => new(false, provider, null, error);
}

/// <summary>
/// Un canal, un adaptateur. Le domaine ne connaît ni opérateur, ni format
/// d'API : il sait seulement qu'un message part, ou ne part pas.
/// </summary>
public interface INotificationSender
{
    NotificationChannel Channel { get; }

    /// <summary>
    /// Vrai si un fournisseur est réellement configuré. Faux fait consigner
    /// l'envoi en « ignoré » au lieu d'« échoué » : la nuance compte quand on
    /// relit le journal six mois plus tard.
    /// </summary>
    bool IsConfigured { get; }

    Task<SendResult> SendAsync(string recipient, string renderedBody, CancellationToken cancellationToken);
}

public interface ISentNotificationRepository
{
    void Add(SentNotification notification);
}
