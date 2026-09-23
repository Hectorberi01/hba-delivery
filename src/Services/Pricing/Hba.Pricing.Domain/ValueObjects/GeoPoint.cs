using System.Globalization;
using Hba.BuildingBlocks.Domain;

namespace Hba.Pricing.Domain.ValueObjects;

/// <summary>Point GPS en WGS84.</summary>
public sealed class GeoPoint : ValueObject
{
    private GeoPoint()
    {
    }

    private GeoPoint(double latitude, double longitude)
    {
        Latitude = latitude;
        Longitude = longitude;
    }

    public double Latitude { get; private set; }

    public double Longitude { get; private set; }

    public static GeoPoint Create(double latitude, double longitude)
    {
        if (latitude is < -90 or > 90)
        {
            throw new DomainException("INVALID_COORDINATES", $"Latitude hors bornes : {latitude}.");
        }

        if (longitude is < -180 or > 180)
        {
            throw new DomainException("INVALID_COORDINATES", $"Longitude hors bornes : {longitude}.");
        }

        return new GeoPoint(latitude, longitude);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Latitude;
        yield return Longitude;
    }

    public override string ToString()
        => string.Create(CultureInfo.InvariantCulture, $"{Latitude:F6},{Longitude:F6}");
}
