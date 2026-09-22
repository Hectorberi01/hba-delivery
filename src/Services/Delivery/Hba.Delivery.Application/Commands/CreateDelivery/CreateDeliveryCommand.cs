using Hba.BuildingBlocks.Application.Messaging;
using Hba.Delivery.Application.Views;
using Hba.Delivery.Domain.Deliveries;

namespace Hba.Delivery.Application.Commands.CreateDelivery;

/// <summary>
/// Création d'une livraison par un client, un commerçant ou un partenaire B2B.
/// Le prix n'est pas un paramètre : il vient du devis.
/// </summary>
public sealed record CreateDeliveryCommand : ICommand<CreateDeliveryResult>
{
    /// <summary>Obligatoire pour les partenaires. Recommandé partout ailleurs.</summary>
    public string? IdempotencyKey { get; init; }

    public required string QuoteId { get; init; }

    public required DeliverySource Source { get; init; }

    public string? ExternalOrderId { get; init; }

    public string? MerchantId { get; init; }

    public string? PickupPointId { get; init; }

    public required LocationInput Pickup { get; init; }

    public required LocationInput Dropoff { get; init; }

    public required string RecipientName { get; init; }

    public required string RecipientPhone { get; init; }

    public string? PackageDescription { get; init; }

    public int PackageWeightGrams { get; init; }
}

public sealed record LocationInput(
    double Latitude,
    double Longitude,
    string Landmark,
    string Phone,
    string ContactName,
    string? Notes);

public sealed record CreateDeliveryResult(
    DeliveryView Delivery,
    string? PaymentIntentId,
    string? PaymentRedirectUrl);
