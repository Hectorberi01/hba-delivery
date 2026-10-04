using Hba.Rating.Domain.Reviews;

namespace Hba.Rating.Application.Common;

/// <summary>Les avis, tels que ce service les range.</summary>
public interface IReviewRepository
{
    void Add(Review review);

    Task<Review?> GetAsync(MissionType missionType, Guid referenceId, CancellationToken cancellationToken);

    /// <summary>Les missions que ce client peut encore noter, les plus recentes d'abord.</summary>
    Task<IReadOnlyList<Review>> FindPendingForCustomerAsync(
        string customerId,
        int limit,
        CancellationToken cancellationToken);

    /// <summary>Les avis RECUS par ce livreur, les plus recents d'abord. Notes seulement.</summary>
    Task<IReadOnlyList<Review>> FindSubmittedForDriverAsync(
        Guid driverId,
        int limit,
        CancellationToken cancellationToken);

    /// <summary>
    /// La moyenne et le nombre d'avis d'un livreur.
    /// </summary>
    ///
    /// <remarks>
    /// CALCULEE EN BASE, PAS EN MEMOIRE. Charger tous les avis d'un livreur pour
    /// en faire la moyenne marche a dix avis et plus a dix mille — et c'est un
    /// appel que l'application livreur fait a chaque ouverture.
    /// </remarks>
    Task<(double Moyenne, int Nombre)> ResumerPourDriverAsync(
        Guid driverId,
        CancellationToken cancellationToken);
}
