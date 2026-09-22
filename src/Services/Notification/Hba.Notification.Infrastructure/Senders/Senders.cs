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
    /// À TRANCHER : aucun opérateur SMS n'est choisi. Le référentiel des
    /// acteurs n'en nomme aucun, et le choix a des conséquences — coût par
    /// message, couverture des réseaux béninois, délai de remise, accusé de
    /// réception. Tant qu'il n'est pas fait, aucun adaptateur réel n'est écrit.
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

    public Task<SendResult> SendAsync(string recipient, string renderedBody, CancellationToken cancellationToken)
    {
        logger.LogInformation("SMS (journalisé, non envoyé) vers {Recipient} : {Body}", recipient, renderedBody);

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

    public Task<SendResult> SendAsync(string recipient, string renderedBody, CancellationToken cancellationToken)
        => Task.FromResult(SendResult.Failed("aucun", $"Aucun fournisseur configuré pour {Channel}."));
}
