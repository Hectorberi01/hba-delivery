using Hba.BuildingBlocks.Application.Messaging;
using Hba.Delivery.Application.Views;

namespace Hba.Delivery.Application.Commands.DriverActions;

/// <summary>
/// Actions du livreur. Toutes portent un horodatage client et une clé
/// d'idempotence : l'app livreur met ses actions en file quand la connexion est
/// instable et les rejoue telles quelles.
/// </summary>
public sealed record MarkArrivedAtPickupCommand(
    Guid DeliveryId,
    DateTimeOffset? OccurredAt,
    string? IdempotencyKey) : ICommand<DeliveryView>;

public sealed record MarkPickedUpCommand(
    Guid DeliveryId,
    DateTimeOffset? OccurredAt,
    string? IdempotencyKey,
    string? ProofObjectKey) : ICommand<DeliveryView>;

/// <summary>
/// Remise au destinataire. L'OTP vient du destinataire : il n'est jamais envoyé
/// au livreur, il le saisit.
/// </summary>
public sealed record ConfirmDeliveryCommand(
    Guid DeliveryId,
    string Otp,
    DateTimeOffset? OccurredAt,
    string? IdempotencyKey,
    string? ProofObjectKey) : ICommand<DeliveryView>;
