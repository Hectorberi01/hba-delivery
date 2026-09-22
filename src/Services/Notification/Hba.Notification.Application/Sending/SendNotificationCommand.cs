using Hba.BuildingBlocks.Application.Messaging;
using Hba.Notification.Domain.Messages;

namespace Hba.Notification.Application.Sending;

/// <summary>
/// Un envoi demandé par un autre service. Les variables sont celles du modèle ;
/// Notification ne les interprète pas, il les substitue.
/// </summary>
public sealed record SendNotificationCommand(
    NotificationChannel Channel,
    string Recipient,
    string TemplateId,
    IReadOnlyDictionary<string, string> Variables,
    string? CorrelationId) : ICommand;
