using Hba.BuildingBlocks.Application.Messaging;
using Hba.Driver.Application.Common.Interfaces;
using Hba.Driver.Application.Common.Views;
using Hba.Driver.Domain.Drivers;
using Hba.Driver.Domain.ValueObjects;

namespace Hba.Driver.Application.Features.Drivers.Queries;

/// <summary>
/// Livreurs joignables autour d'un point, pour une vague de dispatch.
/// </summary>
public sealed record FindAvailableNearbyQuery(
    double Latitude,
    double Longitude,
    int RadiusMeters,
    VehicleType? VehicleType,
    int Limit,
    IReadOnlyCollection<Guid> ExcludeDriverIds) : IQuery<IReadOnlyList<NearbyDriverView>>;

public sealed class FindAvailableNearbyHandler(
    IDriverRepository drivers,
    IDriverLocationStore locations)
    : IQueryHandler<FindAvailableNearbyQuery, IReadOnlyList<NearbyDriverView>>
{
    /// <summary>
    /// Facteur de surinterrogation de Redis.
    ///
    /// LA GEOGRAPHIE NE SAIT PAS QUI EST DISPONIBLE. Redis rend les positions
    /// les plus proches ; la base ecarte ensuite ceux qui sont hors ligne, en
    /// mission, non verifies, ou sur le mauvais vehicule. Demander exactement
    /// le nombre voulu rendrait donc presque toujours moins. On en demande
    /// davantage et on tronque apres le croisement.
    /// </summary>
    private const int OverFetch = 5;

    private const int MinimumCandidates = 50;

    public async Task<IReadOnlyList<NearbyDriverView>> HandleAsync(
        FindAvailableNearbyQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (query.Limit <= 0 || query.RadiusMeters <= 0)
        {
            return [];
        }

        var candidates = await locations
            .SearchAsync(
                GeoPoint.Create(query.Latitude, query.Longitude),
                query.RadiusMeters,
                Math.Max(query.Limit * OverFetch, MinimumCandidates),
                cancellationToken)
            .ConfigureAwait(false);

        var excluded = query.ExcludeDriverIds as IReadOnlySet<Guid> ?? query.ExcludeDriverIds.ToHashSet();

        var retenus = candidates.Where(c => !excluded.Contains(c.DriverId)).ToList();
        if (retenus.Count == 0)
        {
            return [];
        }

        var eligibles = await drivers
            .FindAvailableAsync([.. retenus.Select(c => c.DriverId)], query.VehicleType, cancellationToken)
            .ConfigureAwait(false);

        var autorises = eligibles.Select(d => d.Id).ToHashSet();

        // L'ORDRE VIENT DE REDIS, PAS DE LA BASE : la recherche geographique
        // rend deja du plus proche au plus lointain, et c'est cet ordre que la
        // vague doit suivre.
        return
        [
            .. retenus
                .Where(c => autorises.Contains(c.DriverId))
                .Take(query.Limit)
                .Select(c => new NearbyDriverView(c.DriverId, c.DistanceMeters, c.Latitude, c.Longitude)),
        ];
    }
}
