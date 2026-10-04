using Hba.BuildingBlocks.Domain;

namespace Hba.Ride.Domain.ValueObjects;

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

    /// <summary>Vrai si le couple décrit un point du globe.</summary>
    public static bool EstValide(double latitude, double longitude)
        => !double.IsNaN(latitude) && !double.IsNaN(longitude)
           && latitude is >= -90 and <= 90
           && longitude is >= -180 and <= 180;

    /// <summary>
    /// Construit un point.
    ///
    /// CETTE MÉTHODE LÈVE, ELLE NE REND PAS DE Result, ET CE N'EST PAS UNE
    /// ENTORSE. Un objet de valeur invalide ne doit pas exister : le laisser
    /// naître pour le refuser plus loin déplace le problème. La validation du
    /// flux métier, elle, se fait AVANT, avec <see cref="EstValide"/>, et rend
    /// un refus propre.
    /// </summary>
    public static GeoPoint Create(double latitude, double longitude)
        => EstValide(latitude, longitude)
            ? new GeoPoint(latitude, longitude)
            : throw new ArgumentOutOfRangeException(
                nameof(latitude),
                "Coordonnées hors du globe : validez avec EstValide avant de construire.");

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Latitude;
        yield return Longitude;
    }
}
