using Hba.Pricing.Domain.Quotes;
using Hba.Pricing.Domain.ValueObjects;

namespace Hba.Pricing.Application.Common.Interfaces;

/// <summary>
/// Mesure un trajet entre deux points.
///
/// C'EST UN PORT, PAS UN DETAIL. Le contrat protobuf annonce « Distance et
/// duree renvoyees par OSRM » et RouteMeasurement previent qu'une distance a
/// vol d'oiseau sous-facture les courses qui contournent la lagune. L'adaptateur
/// pose aujourd'hui est justement celui-la, a la demande explicite du produit :
/// l'interface existe pour qu'OSRM le remplace sans qu'une ligne du domaine ni
/// du handler ne bouge.
/// </summary>
public interface IRouteEngine
{
    Task<RouteMeasurement> MeasureAsync(GeoPoint pickup, GeoPoint dropoff, CancellationToken cancellationToken);
}
