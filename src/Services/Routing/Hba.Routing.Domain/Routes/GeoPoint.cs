using Hba.BuildingBlocks.Domain;
using Hba.Routing.Domain.Exceptions;

namespace Hba.Routing.Domain.Routes;

/// <summary>Point GPS en WGS84.</summary>
///
/// <remarks>
/// CHAQUE SERVICE A LE SIEN, ET CE N'EST PAS DE LA DUPLICATION. Le domaine ne
/// dépend pas des contrats générés — c'est la règle du dépôt, que Pricing et
/// Dispatch suivent déjà. Un GeoPoint partagé ferait dépendre cinq domaines d'un
/// même fichier protobuf, et le premier changement de contrat les recompilerait
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
                RoutingErrorCodes.InvalidCoordinates,
                "Coordonnées hors du globe.");
        }

        return new GeoPoint(latitude, longitude);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Latitude;
        yield return Longitude;
    }
}
