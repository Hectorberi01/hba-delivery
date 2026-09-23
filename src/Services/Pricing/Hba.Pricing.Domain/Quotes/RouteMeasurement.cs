using Hba.BuildingBlocks.Domain;

namespace Hba.Pricing.Domain.Quotes;

/// <summary>
/// Distance et durée d'un trajet, telles que les renvoie le moteur de routage.
/// Ni l'une ni l'autre n'est estimée par Pricing : une distance à vol d'oiseau
/// sous-facturerait toutes les courses qui contournent la lagune.
/// </summary>
public sealed class RouteMeasurement : ValueObject
{
    private RouteMeasurement()
    {
    }

    private RouteMeasurement(int distanceMeters, int durationSeconds)
    {
        DistanceMeters = distanceMeters;
        DurationSeconds = durationSeconds;
    }

    public int DistanceMeters { get; private set; }

    public int DurationSeconds { get; private set; }

    public static RouteMeasurement Create(int distanceMeters, int durationSeconds)
    {
        if (distanceMeters < 0 || durationSeconds < 0)
        {
            throw new DomainException("INVALID_ROUTE", "Distance et durée doivent être positives.");
        }

        return new RouteMeasurement(distanceMeters, durationSeconds);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return DistanceMeters;
        yield return DurationSeconds;
    }
}
