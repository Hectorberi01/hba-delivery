using Hba.BuildingBlocks.Domain;
using Hba.Pricing.Domain.Tariffs;
using Hba.Pricing.Domain.ValueObjects;

namespace Hba.Pricing.Domain.Quotes;

/// <summary>
/// Prix proposé pour un trajet, valable un temps borné et consommable une seule
/// fois. Un devis consommé est recopié dans la livraison et figé : c'est la
/// raison d'être de l'ADR 0004.
/// </summary>
public sealed class Quote : AggregateRoot
{
    private Quote()
    {
    }

    private Quote(
        Guid id,
        string tariffVersion,
        string zoneCode,
        VehicleType vehicleType,
        GeoPoint pickup,
        GeoPoint dropoff,
        RouteMeasurement route,
        QuoteAmounts amounts,
        DateTimeOffset createdAt,
        DateTimeOffset expiresAt)
        : base(id)
    {
        TariffVersion = tariffVersion;
        ZoneCode = zoneCode;
        VehicleType = vehicleType;
        Pickup = pickup;
        Dropoff = dropoff;
        Route = route;
        Total = amounts.Total;
        BaseFare = amounts.BaseFare;
        VariableFare = amounts.VariableFare;
        SurgeFare = amounts.SurgeFare;
        DriverEarning = amounts.DriverEarning;
        CreatedAt = createdAt;
        ExpiresAt = expiresAt;
    }

    public string TariffVersion { get; private set; } = string.Empty;

    public string ZoneCode { get; private set; } = string.Empty;

    public VehicleType VehicleType { get; private set; }

    public GeoPoint Pickup { get; private set; } = null!;

    public GeoPoint Dropoff { get; private set; } = null!;

    public RouteMeasurement Route { get; private set; } = null!;

    public MoneyXof Total { get; private set; } = null!;

    public MoneyXof BaseFare { get; private set; } = null!;

    public MoneyXof VariableFare { get; private set; } = null!;

    public MoneyXof SurgeFare { get; private set; } = null!;

    public MoneyXof DriverEarning { get; private set; } = null!;

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset? ConsumedAt { get; private set; }

    /// <summary>Livraison qui a consommé ce devis. Nul tant qu'il ne l'est pas.</summary>
    public Guid? ConsumedByDeliveryId { get; private set; }

    public bool IsConsumed => ConsumedByDeliveryId is not null;

    public bool HasExpiredAt(DateTimeOffset instant) => instant >= ExpiresAt;

    public static Quote Create(
        Guid id,
        Tariff tariff,
        GeoPoint pickup,
        GeoPoint dropoff,
        RouteMeasurement route,
        DateTimeOffset createdAt,
        TimeSpan lifetime)
    {
        ArgumentNullException.ThrowIfNull(tariff);
        ArgumentNullException.ThrowIfNull(pickup);
        ArgumentNullException.ThrowIfNull(dropoff);
        ArgumentNullException.ThrowIfNull(route);

        if (lifetime <= TimeSpan.Zero)
        {
            throw new DomainException("INVALID_QUOTE_LIFETIME", "Un devis doit avoir une durée de validité positive.");
        }

        var amounts = QuoteCalculator.Compute(tariff, route);

        return new Quote(
            id,
            tariff.TariffVersion,
            tariff.ZoneCode,
            tariff.VehicleType,
            pickup,
            dropoff,
            route,
            amounts,
            createdAt,
            createdAt.Add(lifetime));
    }

    /// <summary>
    /// Consomme le devis au profit d'une livraison.
    ///
    /// IDEMPOTENT PAR LIVRAISON : la création d'une livraison est rejouable, et
    /// un rejeu ne doit pas se heurter à « devis déjà consommé » alors que
    /// c'est la même livraison qui redemande. Une AUTRE livraison, elle, est
    /// refusée.
    /// </summary>
    public void Consume(Guid deliveryId, DateTimeOffset now)
    {
        if (deliveryId == Guid.Empty)
        {
            throw new DomainException("MISSING_DELIVERY_ID", "La consommation d'un devis nomme la livraison.");
        }

        if (ConsumedByDeliveryId == deliveryId)
        {
            return;
        }

        if (IsConsumed)
        {
            throw new DomainException("QUOTE_ALREADY_CONSUMED", "Ce devis a déjà servi à une autre livraison.");
        }

        if (HasExpiredAt(now))
        {
            throw new DomainException("QUOTE_EXPIRED", "Ce devis a expiré : il faut en demander un nouveau.");
        }

        ConsumedByDeliveryId = deliveryId;
        ConsumedAt = now;
    }
}
