using Hba.BuildingBlocks.Domain;
using Hba.Location.Domain.Exceptions;

namespace Hba.Location.Domain.Positions;

/// <summary>Point GPS en WGS84.</summary>
///
/// <remarks>
/// CHAQUE SERVICE A LE SIEN, ET CE N'EST PAS DE LA DUPLICATION. Le domaine ne
/// depend pas des contrats generes — c'est la regle du depot, que Pricing, Dispatch
/// et Routing suivent deja. Un GeoPoint partage ferait dependre six domaines d'un
/// meme fichier protobuf, et le premier changement de contrat les recompilerait
/// tous.
/// </remarks>
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
        if (double.IsNaN(latitude) || double.IsNaN(longitude)
            || latitude is < -90 or > 90 || longitude is < -180 or > 180)
        {
            throw new DomainException(
                LocationErrorCodes.InvalidCoordinates,
                "Coordonnees hors du globe.");
        }

        return new GeoPoint(latitude, longitude);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Latitude;
        yield return Longitude;
    }
}
