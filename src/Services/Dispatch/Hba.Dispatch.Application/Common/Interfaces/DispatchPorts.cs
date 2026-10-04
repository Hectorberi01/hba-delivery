using Hba.Dispatch.Domain.Dispatching;
using Hba.Dispatch.Domain.ValueObjects;

namespace Hba.Dispatch.Application.Common.Interfaces;

public interface IDispatchRepository
{
    /// <summary>
    /// La recherche d'une mission. LES DEUX ARGUMENTS SONT NECESSAIRES : deux
    /// missions de natures differentes peuvent porter le meme identifiant, et
    /// chercher sur l'identifiant seul rendrait l'une pour l'autre.
    /// </summary>
    Task<DispatchAggregate?> GetByReferenceAsync(
        ServiceType serviceType,
        Guid referenceId,
        CancellationToken cancellationToken);

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
    /// <summary>
    /// « Qui est joignable autour de ce point, habilite a CETTE mission ? »
    /// </summary>
    ///
    /// <remarks>
    /// L'HABILITATION ET LE VEHICULE SONT PASSES A DRIVER, PAS FILTRES ICI. Lui
    /// seul connait les dossiers ; filtrer apres coup rendrait des vagues plus
    /// maigres que demande, exactement comme filtrer la liste d'exclusion apres
    /// coup le faisait.
    ///
    /// UN VEHICULE NON PRECISE VEUT DIRE « N'IMPORTE LEQUEL ». C'est le cas de
    /// toutes les livraisons aujourd'hui.
    /// </remarks>
    Task<IReadOnlyList<CandidateDriver>> FindAvailableNearbyAsync(
        GeoPoint center,
        int radiusMeters,
        int limit,
        DriverCapability requiredCapability,
        VehicleType vehicleType,
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
    /// <summary>
    /// Rend null si le verrou est deja tenu. Le jeton sert a le rendre.
    ///
    /// LA CLE PORTE LA NATURE EN PLUS DE L'IDENTIFIANT. Un identifiant seul
    /// ferait partager un verrou a deux missions de natures differentes qui
    /// porteraient le meme : improbable avec des UUID v7, et indetectable le jour
    /// ou cela arriverait.
    /// </summary>
    Task<string?> TryAcquireAsync(
        ServiceType serviceType,
        Guid referenceId,
        TimeSpan duration,
        CancellationToken cancellationToken);

    Task ReleaseAsync(ServiceType serviceType, Guid referenceId, string token, CancellationToken cancellationToken);
}

/// <summary>
/// Acces au service Routing, pour classer les candidats par temps de trajet reel
/// jusqu'au point de prise en charge.
/// </summary>
///
/// <remarks>
/// C'EST L'ETAPE 5 DE L'ALGORITHME DEMANDE, et elle change le gagnant : la
/// recherche de proximite ordonne a VOL D'OISEAU, et le plus proche a vol
/// d'oiseau n'est pas le plus proche en temps — une lagune, un pont, un sens
/// unique suffisent a inverser l'ordre.
///
/// ELLE NE DOIT JAMAIS BLOQUER UNE VAGUE. Routing indisponible, ou rendant une
/// mesure degradee, ne change rien au fait qu'il faut proposer la course a
/// quelqu'un : on garde alors l'ordre de la recherche de proximite, qui est la
/// meme mesure. Voir le point 36 — une mesure degradee doit se savoir, pas
/// s'arreter.
/// </remarks>
public interface IEtaRanker
{
    /// <summary>
    /// Les candidats, classes du plus rapide au plus lent. Rend la liste
    /// d'origine, dans son ordre, si aucune mesure routiere n'est disponible.
    /// </summary>
    Task<IReadOnlyList<CandidateDriver>> ClasserAsync(
        IReadOnlyList<CandidateDriver> candidats,
        GeoPoint pickup,
        VehicleType vehicleType,
        CancellationToken cancellationToken);
}
