using Hba.BuildingBlocks.Application.Messaging;
using Hba.Delivery.Domain.Deliveries;

namespace Hba.Delivery.Application.Commands.Internal;

// Commandes déclenchées par des acteurs système, depuis les consommateurs Kafka.
// Elles ne passent pas par un JWT : l'acteur est imposé par le code, et c'est
// lui qui apparaît dans l'audit.

/// <summary>Webhook FedaPay relayé par le service Payment.</summary>
public sealed record ConfirmPaymentCommand(
    Guid DeliveryId,
    string PaymentIntentId,
    DateTimeOffset PaidAt) : ICommand;

public sealed record FailPaymentCommand(
    Guid DeliveryId,
    string Reason,
    DateTimeOffset OccurredAt) : ICommand;

/// <summary>Première vague d'offres envoyée par le moteur de dispatch.</summary>
public sealed record StartDriverSearchCommand(Guid DeliveryId, DateTimeOffset OccurredAt) : ICommand;

/// <summary>Offre acceptée : le gagnant est déjà départagé côté Dispatch.</summary>
public sealed record AssignDriverCommand(
    Guid DeliveryId,
    string DriverId,
    string OfferId,
    DateTimeOffset AssignedAt) : ICommand;

public sealed record MarkNoDriverFoundCommand(
    Guid DeliveryId,
    int WavesAttempted,
    DateTimeOffset OccurredAt) : ICommand;

/// <summary>Réaffectation forcée demandée par ops.</summary>
public sealed record UnassignDriverCommand(
    Guid DeliveryId,
    string Reason,
    DateTimeOffset OccurredAt,
    string AdminId) : ICommand;

/// <summary>Statuts que le back-office peut viser lors d'une clôture forcée.</summary>
public static class AdminClosableStatuses
{
    public static readonly IReadOnlySet<DeliveryStatus> Values = new HashSet<DeliveryStatus>
    {
        DeliveryStatus.Cancelled,
        DeliveryStatus.Failed,
    };
}
