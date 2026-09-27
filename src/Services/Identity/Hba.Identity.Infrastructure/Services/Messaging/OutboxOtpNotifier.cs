using System.Globalization;
using Google.Protobuf.WellKnownTypes;
using Hba.BuildingBlocks.Messaging.Kafka;
using Hba.BuildingBlocks.Messaging.Outbox;
using Hba.Contracts.Common.V1;
using Hba.Contracts.Notification.V1;
using Hba.Identity.Application.Common.Interfaces;

namespace Hba.Identity.Infrastructure.Services.Messaging;

/// <summary>
/// Identity ne parle à aucun opérateur téléphonique. Il dépose une commande
/// d'envoi dans son Outbox, à destination du service Notification, qui choisit
/// le canal — SMS ou WhatsApp — et le modèle de message.
///
/// La commande part sur hba.notification.commands.v1, pas sur le topic des
/// événements d'identité : le code n'a aucune raison d'être lisible par les
/// services qui écoutent les créations de compte.
/// </summary>
internal sealed class OutboxOtpNotifier(IOutbox outbox) : IOtpNotifier
{
    public const string TemplateId = "otp_login";

    public void SendOtp(string phone, string code, TimeSpan validity, bool whatsAppAllowed)
    {
        var minutes = ((int)validity.TotalMinutes).ToString(CultureInfo.InvariantCulture);

        // DEUX CHARGES UTILES, DEUX SENS DIFFERENTS. SendWhatsApp ne veut pas
        // dire « envoie par WhatsApp » : il veut dire « le consentement existe,
        // tu peux suivre la chaîne du catalogue », c'est-à-dire WhatsApp puis
        // SMS en repli. SendSms, lui, impose le SMS et n'ouvre aucun repli.
        // Un code de connexion qui ne part pas laisse quelqu'un dehors.
        var command = whatsAppAllowed
            ? BuildWhatsApp(phone, code, minutes)
            : BuildSms(phone, code, minutes);

        // Clé de partition : le numéro. Deux codes pour le même téléphone
        // arrivent dans l'ordre où ils ont été demandés.
        outbox.Enqueue(KafkaTopics.NotificationCommands, phone, command.Envelope.EventType, command);
    }

    private static NotificationCommand BuildWhatsApp(string phone, string code, string minutes)
    {
        var command = new NotificationCommand
        {
            Envelope = Envelope(phone, "hba.notification.v1.SendWhatsApp"),
            SendWhatsapp = new SendWhatsApp
            {
                ToPhone = phone,
                TemplateId = TemplateId,
            },
        };

        command.SendWhatsapp.Variables.Add("code", code);
        command.SendWhatsapp.Variables.Add("minutes", minutes);

        return command;
    }

    private static NotificationCommand BuildSms(string phone, string code, string minutes)
    {
        var command = new NotificationCommand
        {
            Envelope = Envelope(phone, "hba.notification.v1.SendSms"),
            SendSms = new SendSms
            {
                ToPhone = phone,
                TemplateId = TemplateId,
            },
        };

        command.SendSms.Variables.Add("code", code);
        command.SendSms.Variables.Add("minutes", minutes);

        return command;
    }

    private static EventEnvelope Envelope(string phone, string eventType)
    {
        return new EventEnvelope
            {
                EventId = Guid.CreateVersion7().ToString(),
                EventType = eventType,
                SchemaVersion = 1,
                AggregateType = "otp_challenge",
                AggregateId = phone,
                OccurredAt = Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow),
                TraceId = System.Diagnostics.Activity.Current?.TraceId.ToString() ?? string.Empty,
                CorrelationId = System.Diagnostics.Activity.Current?.RootId ?? string.Empty,
                ActorType = ActorType.Unspecified,
                ActorId = "identity",
                Producer = "identity",
            };
    }
}
