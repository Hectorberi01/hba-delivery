using Hba.Notification.Application.Ports;
using Hba.Notification.Domain.Messages;
using Microsoft.Extensions.Logging;

namespace Hba.Notification.Infrastructure.Senders;

public sealed class SmsOptions
{
    public const string SectionName = "Sms";

    /// <summary>
    /// « log » écrit le message dans les journaux — utile en développement,
    /// inacceptable ailleurs puisque les codes y apparaissent en clair.
    /// « none » ne fait rien et consigne l'envoi comme ignoré.
    ///
    /// À TRANCHER : aucun opérateur SMS n'est choisi. Le SMS n'est plus le
    /// canal principal du code de connexion — WhatsApp l'est (ADR 0014) — mais
    /// il reste le SEUL canal vers le destinataire d'un colis, qui n'a pas de
    /// compte et ne peut donc pas donner d'opt-in. Un opérateur reste donc
    /// indispensable.
    /// </summary>
    public string Provider { get; set; } = "none";

    /// <summary>Nom de l'expéditeur affiché. Souvent soumis à déclaration.</summary>
    public string SenderId { get; set; } = "HBA";
}

/// <summary>
/// Écrit le message dans les journaux au lieu de l'envoyer. Le contenu rendu y
/// apparaît EN CLAIR, codes compris : c'est précisément l'intérêt en
/// développement, et la raison pour laquelle ce fournisseur refuse de
/// s'activer ailleurs.
/// </summary>
public sealed class LoggingSmsSender(ILogger<LoggingSmsSender> logger) : INotificationSender
{
    public NotificationChannel Channel => NotificationChannel.Sms;

    public bool IsConfigured => true;

    public Task<SendResult> SendAsync(
        string recipient,
        OutboundMessage message,
        CancellationToken cancellationToken)
    {
        var body = message is TextMessage text ? text.Body : message.ToString();

        logger.LogInformation("SMS (journalisé, non envoyé) vers {Recipient} : {Body}", recipient, body);

        return Task.FromResult(SendResult.Ok("log", Guid.CreateVersion7().ToString()));
    }
}

/// <summary>
/// Canal reconnu, mais sans fournisseur. Il existe pour que le système dise
/// « je n'ai pas envoyé, et voici pourquoi » plutôt que de laisser croire à un
/// envoi réussi.
/// </summary>
public sealed class UnconfiguredSender(NotificationChannel channel) : INotificationSender
{
    public NotificationChannel Channel { get; } = channel;

    public bool IsConfigured => false;

    public Task<SendResult> SendAsync(
        string recipient,
        OutboundMessage message,
        CancellationToken cancellationToken)
        => Task.FromResult(SendResult.Failed("aucun", $"Aucun fournisseur configuré pour {Channel}."));
}
