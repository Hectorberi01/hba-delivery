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

    /// <summary>
    /// Distance orthodromique en metres, par la formule de haversine.
    /// </summary>
    ///
    /// <remarks>
    /// A VOL D'OISEAU, ET C'EST SUFFISANT ICI. Elle ne sert pas a tarifer — la
    /// tarification exige une distance routee, faute de quoi toutes les courses
    /// qui contournent la lagune seraient sous-facturees (voir
    /// <see cref="Quotes.RouteMeasurement"/>). Elle sert a repondre a une seule
    /// question : « est-ce le meme point ? ». Pour cela, l'ecart a vol d'oiseau
    /// est la bonne mesure, et la seule que Pricing puisse calculer sans appeler
    /// le moteur de routage.
    /// </remarks>
    public double DistanceEnMetresVers(GeoPoint autre)
    {
        ArgumentNullException.ThrowIfNull(autre);

        const double rayonTerrestreMetres = 6_371_000;

        var phi1 = double.DegreesToRadians(Latitude);
        var phi2 = double.DegreesToRadians(autre.Latitude);
        var deltaPhi = phi2 - phi1;
        var deltaLambda = double.DegreesToRadians(autre.Longitude - Longitude);

        var a = (Math.Sin(deltaPhi / 2) * Math.Sin(deltaPhi / 2))
                + (Math.Cos(phi1) * Math.Cos(phi2) * Math.Sin(deltaLambda / 2) * Math.Sin(deltaLambda / 2));

        return 2 * rayonTerrestreMetres * Math.Asin(Math.Min(1, Math.Sqrt(a)));
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Latitude;
        yield return Longitude;
    }

    public override string ToString()
        => string.Create(CultureInfo.InvariantCulture, $"{Latitude:F6},{Longitude:F6}");
}
