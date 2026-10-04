using Hba.BuildingBlocks.Application.Messaging;
using Hba.BuildingBlocks.Messaging.Kafka;
using Hba.Contracts.Notification.V1;
using Hba.Notification.Application.Sending;
using Hba.Notification.Domain.Messages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

// LE CANAL PORTE LE MEME NOM DES DEUX COTES, ET C'EST VOULU : le contrat
// protobuf nomme les canaux comme le domaine. Ce fichier importe les deux
// espaces de noms — le proto pour lire la commande Kafka, le domaine pour
// construire la commande applicative — donc « NotificationChannel » seul est
// ambigu depuis que notification_service.proto existe (2 octobre 2026). ICI,
// C'EST TOUJOURS CELUI DU DOMAINE : ce consommateur traduit du proto vers le
// domaine, jamais l'inverse.
using NotificationChannel = Hba.Notification.Domain.Messages.NotificationChannel;

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
            // LE NUMERO OU LE COMPTE, ET LE PREMIER GAGNE. Identity connait le
            // telephone a qui il envoie un code ; Delivery et Ride ne connaissent
            // que le compte de leur client, et c'est Notification qui demande son
            // numero a l'annuaire. Sans ce second chemin, deux modeles du catalogue
            // sont restes sans producteur depuis l'origine.
            NotificationCommand.PayloadOneofCase.SendSms => new SendNotificationCommand(
                NotificationChannel.Sms,
                message.SendSms.ToPhone,
                message.SendSms.TemplateId,
                Copy(message.SendSms.Variables),
                correlationId,
                Guid.TryParse(message.SendSms.SubjectId, out var compteSms) ? compteSms : null,
                message.Envelope?.EventId),

            // CANAL NON IMPOSE, DELIBEREMENT. Demander WhatsApp, c'est demander la
            // chaine du catalogue — WhatsApp puis SMS —, pas WhatsApp seul : un
            // code de connexion qui ne part pas bloque l'utilisateur dehors.
            // Le SMS, lui, reste impose : un destinataire n'a pas d'opt-in.
            NotificationCommand.PayloadOneofCase.SendWhatsapp => new SendNotificationCommand(
                null,
                message.SendWhatsapp.ToPhone,
                message.SendWhatsapp.TemplateId,
                Copy(message.SendWhatsapp.Variables),
                correlationId,
                Guid.TryParse(message.SendWhatsapp.SubjectId, out var compteWa) ? compteWa : null,
                message.Envelope?.EventId),

            NotificationCommand.PayloadOneofCase.SendPush => new SendNotificationCommand(
                NotificationChannel.Push,
                message.SendPush.SubjectId,
                message.SendPush.TemplateId,
                Copy(message.SendPush.Variables),
                correlationId),

            // LE DESTINATAIRE EST VIDE, ET L'IDENTIFIANT DE COMPTE PORTE TOUT.
            // C'est Notification qui demandera son adresse a Directory : celui
            // qui publie la commande — Delivery, quand une course se termine —
            // ne la connait pas, et la lui faire chercher mettrait un appel
            // reseau dans la transaction qui cloture une livraison.
            NotificationCommand.PayloadOneofCase.SendEmail => new SendNotificationCommand(
                NotificationChannel.Email,
                string.Empty,
                message.SendEmail.TemplateId,
                Copy(message.SendEmail.Variables),
                correlationId,
                Guid.TryParse(message.SendEmail.SubjectId, out var compte) ? compte : null,
                // L'IDENTIFIANT DE L'EVENEMENT, ET NON CELUI DE CORRELATION.
                // Le second suit une requete HTTP et change a chaque rejeu ;
                // le premier est derive de facon deterministe par le service
                // qui publie, donc deux tentatives d'un meme envoi portent la
                // meme cle et le fournisseur n'expedie qu'une fois.
                message.Envelope?.EventId),

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
