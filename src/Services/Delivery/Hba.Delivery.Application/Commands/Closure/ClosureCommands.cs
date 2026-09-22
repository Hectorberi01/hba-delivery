using Hba.BuildingBlocks.Application.Messaging;
using Hba.Delivery.Application.Views;
using Hba.Delivery.Domain.Deliveries;

namespace Hba.Delivery.Application.Commands.Closure;

/// <summary>Annulation par le donneur d'ordre, selon la politique d'annulation.</summary>
public sealed record CancelDeliveryCommand(Guid DeliveryId, string Reason) : ICommand<DeliveryView>;

/// <summary>
/// Clôture forcée par le back-office. Ne peut viser que Failed ou Cancelled :
/// marquer une livraison comme remise appartient au seul livreur, avec l'OTP.
/// </summary>
public sealed record AdminCloseDeliveryCommand(
    Guid DeliveryId,
    DeliveryStatus TargetStatus,
    string Reason) : ICommand<DeliveryView>;
