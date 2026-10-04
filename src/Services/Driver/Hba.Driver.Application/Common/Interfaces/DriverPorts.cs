using Hba.BuildingBlocks.Application.Time;
using Hba.Driver.Domain.Drivers;

namespace Hba.Driver.Application.Common.Interfaces;

public interface IDriverRepository
{
    Task<DriverAggregate?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Les livreurs nommes, sans filtre d'etat.</summary>
    ///
    /// <remarks>
    /// SERT A NOMMER, PAS A DECIDER. La carte de l'exploitation montre aussi les
    /// livreurs hors ligne ou en course : elle ne peut donc pas passer par
    /// <see cref="FindAvailableAsync"/>, qui ne rend que les offrables.
    /// </remarks>
    Task<IReadOnlyList<DriverAggregate>> FindByIdsAsync(
        IReadOnlyCollection<Guid> ids,
        CancellationToken cancellationToken);

    /// <summary>
    /// Livreurs pouvant recevoir une offre parmi une liste d'identifiants :
    /// dossier verifie, etat AVAILABLE, et vehicule compatible si un type est
    /// demande. C'est la lecture du chemin critique du dispatch.
    /// </summary>
    ///
    /// <remarks>
    /// LA LISTE D'IDENTIFIANTS VIENT DE LOCATION, qui a rendu les plus proches. Depuis
    /// le 1er octobre 2026 ce depot ne sait plus faire autrement : Driver n'a plus
    /// d'index geographique, donc plus aucun moyen de produire cette liste lui-meme.
    /// C'est l'appelant qui croise les deux services, et c'est le prix de la frontiere
    /// — Driver repond « qui », Location repond « ou ».
    /// </remarks>
    Task<IReadOnlyList<DriverAggregate>> FindAvailableAsync(
        IReadOnlyCollection<Guid> ids,
        VehicleType? vehicleType,
        CancellationToken cancellationToken);

    /// <summary>
    /// Annuaire du back-office. Rend la page demandee et le total du filtre :
    /// sans le total, l'interface ne peut pas dire « 12 sur 248 », et un
    /// deuxieme aller-retour pour le compter couterait une requete de plus a
    /// chaque frappe dans le champ de recherche.
    /// </summary>
    Task<(IReadOnlyList<DriverAggregate> Drivers, int Total)> ListAsync(
        string? query,
        VerificationStatus? verificationStatus,
        OperationalStatus? operationalStatus,
        int pageSize,
        int offset,
        CancellationToken cancellationToken);

    void Add(DriverAggregate driver);
}

// ---- LES POSITIONS ONT QUITTE CE SERVICE ------------------------------------
//
// IDriverLocationStore, LocatedDriver, DriverPosition et MeasuredPosition vivaient
// ici jusqu'au 1er octobre 2026. Ils sont partis dans le service Location, avec
// l'index Redis GEO qui les portait.
//
// CE QUE CE DEPLACEMENT A COUTE, POUR QUE PERSONNE NE LE REDECOUVRE. La recherche de
// proximite croisait les deux sources DANS UN SEUL HANDLER : Redis donnait les plus
// proches, la base ecartait les non-eligibles, et le tout tenait en un appel. Ce
// croisement est maintenant chez l'APPELANT, qui fait deux appels au lieu d'un.
//
// CE QU'IL A RAPPORTE. Un service qui tient des positions sans tenir un fichier de
// personnes : Location ne connait ni les noms, ni les telephones, ni les pieces
// d'identite. Et une consequence qu'on n'attendait pas — retirer la position d'un
// livreur qui se deconnecte ne porte plus de decision. Avant, l'oublier laissait
// quelqu'un rentre chez lui recevoir des offres ; maintenant la vague croise
// toujours la base, qui le voit hors ligne.

public sealed record VerificationTally(VerificationStatus Status, long Count);

public sealed record OperationalTally(OperationalStatus Status, long Count);

/// <summary>
/// DEUX NATURES DE CHIFFRES, ET ELLES NE SE LISENT PAS PAREIL.
///
/// Les effectifs sont un ETAT, pris a l'instant de la lecture : « combien de
/// livreurs sont disponibles » n'a pas de periode. Les appliquer a une
/// fenetre ne voudrait rien dire — un livreur n'est pas disponible « en
/// septembre », il l'est maintenant.
///
/// Les flux sont un MOUVEMENT sur la fenetre : qui s'est inscrit, quels
/// dossiers ont ete valides.
/// </summary>
public sealed record DriverStatsView(
    TimeWindow Window,
    long Total,
    IReadOnlyList<VerificationTally> ByVerification,
    IReadOnlyList<OperationalTally> ByOperational,
    long RegisteredInWindow,
    long VerifiedInWindow);

public interface IDriverStatsReader
{
    Task<DriverStatsView> ReadAsync(TimeWindow window, CancellationToken cancellationToken);
}
