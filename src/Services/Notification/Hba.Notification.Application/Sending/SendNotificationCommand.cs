using Hba.BuildingBlocks.Application.Messaging;
using Hba.Notification.Domain.Messages;

namespace Hba.Notification.Application.Sending;

/// <summary>
/// Un envoi demandé par un autre service. Les variables sont celles du modèle ;
/// Notification ne les interprète pas, il les substitue.
/// </summary>
public sealed record SendNotificationCommand(
    /// <summary>
    /// Canal imposé par l'appelant, ou nul pour laisser le catalogue choisir
    /// et basculer sur son repli. Imposer un canal, c'est aussi renoncer au
    /// repli : c'est voulu pour les cas où un seul canal est acceptable.
    /// </summary>
    NotificationChannel? Channel,
    string Recipient,
    string TemplateId,
    IReadOnlyDictionary<string, string> Variables,
    string? CorrelationId) : ICommand;
