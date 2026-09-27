using Hba.BuildingBlocks.Domain;

namespace Hba.Dispatch.Domain.ValueObjects;

/// <summary>
/// Point d'enlevement de la course. Le domaine valide des bornes ; la
/// recherche de proximite appartient a Driver, qui tient les positions.
/// </summary>
public sealed class GeoPoint : ValueObject
{
    private GeoPoint(double latitude, double longitude)
    {
        Latitude = latitude;
        Longitude = longitude;
    }

    public double Latitude { get; }

    public double Longitude { get; }

    public static GeoPoint Create(double latitude, double longitude)
    {
        if (latitude is < -90 or > 90)
        {
            throw new DomainException("INVALID_LATITUDE", "La latitude doit tenir entre -90 et 90.");
        }

        if (longitude is < -180 or > 180)
        {
            throw new DomainException("INVALID_LONGITUDE", "La longitude doit tenir entre -180 et 180.");
        }

        return new GeoPoint(latitude, longitude);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Latitude;
        yield return Longitude;
    }
}
