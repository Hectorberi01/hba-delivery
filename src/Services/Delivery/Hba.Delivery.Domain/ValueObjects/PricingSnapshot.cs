using Hba.BuildingBlocks.Domain;

namespace Hba.Delivery.Domain.ValueObjects;

/// <summary>
/// Prix figé au moment de la création de la livraison. Une modification
/// ultérieure d'une grille tarifaire ne change JAMAIS une livraison déjà
/// confirmée : c'est pour cela que le prix est recopié ici et non référencé.
/// </summary>
public sealed class PricingSnapshot : ValueObject
{
    // EF Core ne sait pas passer un type possédé imbriqué à un paramètre de
    // constructeur : il matérialise d'abord l'objet, puis rattache la
    // navigation. D'où ce constructeur sans paramètre, réservé au chargement.
    // Le domaine, lui, passe toujours par Create, qui valide.
    private PricingSnapshot()
    {
    }

    private PricingSnapshot(
        string quoteId,
        string tariffVersion,
        MoneyXof total,
        MoneyXof baseFare,
        MoneyXof distanceFare,
        MoneyXof surgeFare,
        MoneyXof driverEarning,
        int distanceMeters,
        int durationSeconds,
        DateTimeOffset quotedAt)
    {
        QuoteId = quoteId;
        TariffVersion = tariffVersion;
        Total = total;
        BaseFare = baseFare;
        DistanceFare = distanceFare;
        SurgeFare = surgeFare;
        DriverEarning = driverEarning;
        DistanceMeters = distanceMeters;
        DurationSeconds = durationSeconds;
        QuotedAt = quotedAt;
    }

    public string QuoteId { get; private set; } = string.Empty;

    public string TariffVersion { get; private set; } = string.Empty;

    public MoneyXof Total { get; private set; } = null!;

    public MoneyXof BaseFare { get; private set; } = null!;

    public MoneyXof DistanceFare { get; private set; } = null!;

    public MoneyXof SurgeFare { get; private set; } = null!;

    /// <summary>
    /// Rémunération du livreur. C'est la seule part du détail tarifaire qu'il
    /// peut voir ; il ne voit jamais le prix payé par le client.
    /// </summary>
    public MoneyXof DriverEarning { get; private set; } = null!;

    public int DistanceMeters { get; private set; }

    public int DurationSeconds { get; private set; }

    public DateTimeOffset QuotedAt { get; private set; }

    public static PricingSnapshot Create(
        string quoteId,
        string tariffVersion,
        MoneyXof total,
        MoneyXof baseFare,
        MoneyXof distanceFare,
        MoneyXof surgeFare,
        MoneyXof driverEarning,
        int distanceMeters,
        int durationSeconds,
        DateTimeOffset quotedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(quoteId);
        ArgumentNullException.ThrowIfNull(total);
        ArgumentNullException.ThrowIfNull(driverEarning);

        if (total.Amount <= 0)
        {
            throw new DomainException("INVALID_PRICE", "Le prix total d'une livraison doit être strictement positif.");
        }

        if (driverEarning.CompareTo(total) > 0)
        {
            throw new DomainException(
                "INVALID_PRICE",
                "La rémunération du livreur ne peut pas dépasser le prix payé par le donneur d'ordre.");
        }

        if (distanceMeters < 0 || durationSeconds < 0)
        {
            throw new DomainException("INVALID_ROUTE", "Distance et durée doivent être positives.");
        }

        return new PricingSnapshot(
            quoteId,
            tariffVersion,
            total,
            baseFare,
            distanceFare,
            surgeFare,
            driverEarning,
            distanceMeters,
            durationSeconds,
            quotedAt);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return QuoteId;
        yield return TariffVersion;
        yield return Total;
        yield return DriverEarning;
        yield return DistanceMeters;
    }
}
