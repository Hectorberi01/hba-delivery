using Hba.BuildingBlocks.Application.Messaging;
using Hba.BuildingBlocks.Messaging.Kafka;
using Hba.Contracts.Notification.V1;
using Hba.Notification.Application.Sending;
using Hba.Notification.Domain.Messages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Hba.Notification.Api.Messaging;

/// <summary>
/// L'unique entrée du service. Notification n'expose aucune API : on ne lui
/// demande pas d'envoyer un message par un appel synchrone, on lui en dépose
/// un. C'est ce qui permet à Identity ou Delivery de rendre la main sans
/// attendre un opérateur téléphonique.
/// </summary>
public sealed class NotificationCommandsConsumer(
    IServiceScopeFactory scopeFactory,
    IOptions<KafkaOptions> options,
    ILogger<NotificationCommandsConsumer> logger)
    : KafkaConsumerBase<NotificationCommand>(scopeFactory, options, logger)
{
    protected override string Topic => KafkaTopics.NotificationCommands;

    protected override Guid GetEventId(NotificationCommand message)
    {
        ArgumentNullException.ThrowIfNull(message);
        return Guid.TryParse(message.Envelope?.EventId, out var id) ? id : Guid.CreateVersion7();
    }

    protected override string GetEventType(NotificationCommand message)
    {
        ArgumentNullException.ThrowIfNull(message);
        return message.Envelope?.EventType ?? "unknown";
    }

    protected override async Task HandleAsync(
        NotificationCommand message,
        IServiceProvider services,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(services);

        var command = Translate(message);

        if (command is null)
        {
            logger.LogWarning("Commande de notification sans charge utile reconnue, ignorée.");
            return;
        }

        var dispatcher = services.GetRequiredService<IDispatcher>();

        await dispatcher.SendAsync(command, cancellationToken).ConfigureAwait(false);
    }

    private static SendNotificationCommand? Translate(NotificationCommand message)
    {
        var correlationId = message.Envelope?.CorrelationId;

        return message.PayloadCase switch
        {
            NotificationCommand.PayloadOneofCase.SendSms => new SendNotificationCommand(
                NotificationChannel.Sms,
                message.SendSms.ToPhone,
                message.SendSms.TemplateId,
                Copy(message.SendSms.Variables),
                correlationId),

            NotificationCommand.PayloadOneofCase.SendWhatsapp => new SendNotificationCommand(
                NotificationChannel.WhatsApp,
                message.SendWhatsapp.ToPhone,
                message.SendWhatsapp.TemplateId,
                Copy(message.SendWhatsapp.Variables),
                correlationId),

            NotificationCommand.PayloadOneofCase.SendPush => new SendNotificationCommand(
                NotificationChannel.Push,
                message.SendPush.SubjectId,
                message.SendPush.TemplateId,
                Copy(message.SendPush.Variables),
                correlationId),

            _ => null,
        };
    }

    /// <summary>
    /// MapField de protobuf vers un dictionnaire ordinaire : le domaine n'a pas
    /// à dépendre du type de collection d'une bibliothèque de sérialisation.
    /// </summary>
    private static IReadOnlyDictionary<string, string> Copy(IEnumerable<KeyValuePair<string, string>> variables)
        => new Dictionary<string, string>(variables, StringComparer.Ordinal);
}
