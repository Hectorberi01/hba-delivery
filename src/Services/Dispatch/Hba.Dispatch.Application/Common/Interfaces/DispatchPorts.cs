using Hba.Dispatch.Domain.Dispatching;
using Hba.Dispatch.Domain.ValueObjects;

namespace Hba.Dispatch.Application.Common.Interfaces;

public interface IDispatchRepository
{
    Task<DispatchAggregate?> GetByDeliveryIdAsync(Guid deliveryId, CancellationToken cancellationToken);

    Task<DispatchAggregate?> GetByOfferIdAsync(Guid offerId, CancellationToken cancellationToken);

    /// <summary>Offre en attente d'un livreur, pour resynchroniser son application.</summary>
    Task<DispatchAggregate?> FindByPendingOfferForDriverAsync(string driverId, CancellationToken cancellationToken);

    /// <summary>
    /// Recherches ouvertes qui demandent l'attention du planificateur : une
    /// offre echue a eteindre, ou une vague close a faire suivre.
    /// </summary>
    Task<IReadOnlyList<DispatchAggregate>> FindDueAsync(
        DateTimeOffset now,
        int batchSize,
        CancellationToken cancellationToken);

    void Add(DispatchAggregate dispatch);
}

/// <summary>
/// Acces au service Driver. Dispatch ne tient ni position ni disponibilite :
/// il demande « qui est joignable autour de ce point » et se fie a la reponse.
/// </summary>
public interface IDriverFinder
{
    Task<IReadOnlyList<CandidateDriver>> FindAvailableNearbyAsync(
        GeoPoint center,
        int radiusMeters,
        int limit,
        IReadOnlyCollection<string> excludeDriverIds,
        CancellationToken cancellationToken);

    /// <summary>
    /// « CE livreur peut-il recevoir une offre, et a quelle distance de ce
    /// point se trouve-t-il ? »
    ///
    /// LA RECHERCHE DE PROXIMITE NE REPOND PAS A CETTE QUESTION. Elle rend
    /// les N plus proches ; aucun rayon, aucune limite ne garantit qu'un
    /// livreur nomme figure dans sa reponse. L'affectation manuelle a besoin
    /// des deux reponses a la fois, et la distance doit etre MESUREE : un
    /// zero de remplissage se lirait « sur place ».
    /// </summary>
    Task<DriverAvailability> CheckAsync(
        string driverId,
        GeoPoint reference,
        CancellationToken cancellationToken);
}

/// <summary>
/// Verdict de Driver sur un livreur nomme. <paramref name="Reason"/> est
/// renseigne quand <paramref name="Offerable"/> est faux ; la distance n'a de
/// sens que dans le cas contraire.
/// </summary>
public sealed record DriverAvailability(
    bool Offerable,
    string? Reason,
    int DistanceToPickupMeters);

/// <summary>
/// Verrou d'acceptation.
///
/// IL NE REMPLACE PAS LA CONCURRENCE OPTIMISTE, il l'economise. Deux livreurs
/// qui appuient dans la meme seconde arriveraient tous deux jusqu'a la base,
/// et l'un des deux verrait une erreur de concurrence brute. Le verrou en
/// arrete un plus tot, avec un refus qui a du sens pour lui — ALREADY_TAKEN.
/// Si le verrou expire au mauvais moment, le jeton xmin tranche derriere.
/// </summary>
public interface IAcceptanceLock
{
    /// <summary>Rend null si le verrou est deja tenu. Le jeton sert a le rendre.</summary>
    Task<string?> TryAcquireAsync(Guid deliveryId, TimeSpan duration, CancellationToken cancellationToken);

    Task ReleaseAsync(Guid deliveryId, string token, CancellationToken cancellationToken);
}
