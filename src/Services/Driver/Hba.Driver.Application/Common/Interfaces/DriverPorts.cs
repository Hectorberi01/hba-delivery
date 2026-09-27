using Hba.BuildingBlocks.Application.Time;
using Hba.Driver.Domain.Drivers;
using Hba.Driver.Domain.ValueObjects;

namespace Hba.Driver.Application.Common.Interfaces;

public interface IDriverRepository
{
    Task<DriverAggregate?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Livreurs pouvant recevoir une offre parmi une liste d'identifiants :
    /// dossier verifie, etat AVAILABLE, et vehicule compatible si un type est
    /// demande. C'est la lecture du chemin critique du dispatch.
    /// </summary>
    /// <summary>Les livreurs nommes, sans filtre d'etat.</summary>
    Task<IReadOnlyList<DriverAggregate>> FindByIdsAsync(
        IReadOnlyCollection<Guid> ids,
        CancellationToken cancellationToken);

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

/// <summary>Livreur trouve par la recherche de proximite, avec sa distance.</summary>
public sealed record LocatedDriver(Guid DriverId, int DistanceMeters, double Latitude, double Longitude);

/// <summary>
/// Position courante d'un livreur, sans distance.
///
/// PAS DE LocatedDriver ICI, ET L'ABSENCE DE DISTANCE EN EST LA RAISON. Une
/// distance suppose un point de reference ; une carte d'exploitation n'en a
/// pas. Reutiliser LocatedDriver obligerait a y mettre zero, et zero metre
/// se lit comme « sur place », ce qui est faux.
/// </summary>
public sealed record DriverPosition(Guid DriverId, double Latitude, double Longitude, DateTimeOffset SeenAt);

/// <summary>
/// Position d'un livreur NOMME, mesuree depuis un point de reference.
///
/// POURQUOI UN TROISIEME ENREGISTREMENT PLUTOT QU'UN CHAMP DE PLUS.
/// LocatedDriver sort d'une recherche de proximite et n'a pas d'age ;
/// DriverPosition sort de la carte et n'a pas de distance, faute de point de
/// reference. L'affectation manuelle a besoin des deux a la fois : a quelle
/// distance du retrait se tient CE livreur, et quand on l'a vu pour la
/// derniere fois. Les fusionner obligerait l'un des deux appelants a porter
/// un champ qu'il ne sait pas remplir — et un zero de remplissage se lirait
/// comme une mesure.
/// </summary>
public sealed record MeasuredPosition(
    Guid DriverId,
    double Latitude,
    double Longitude,
    int DistanceMeters,
    DateTimeOffset SeenAt);

/// <summary>
/// Positions des livreurs.
///
/// DEUX SOURCES DE VERITE, ET C'EST VOULU : l'etat du livreur est en base, sa
/// position est dans Redis. Les melanger reviendrait soit a ecrire un point
/// toutes les cinq secondes dans Postgres, soit a tenir l'etat operationnel
/// dans un cache qui peut disparaitre. Chacune des deux est autoritaire sur ce
/// qu'elle sait, et la recherche croise les deux.
/// </summary>
public interface IDriverLocationStore
{
    Task UpsertAsync(Guid driverId, GeoPoint position, DateTimeOffset capturedAt, CancellationToken cancellationToken);

    /// <summary>Retire la position : le livreur passe hors ligne.</summary>
    Task RemoveAsync(Guid driverId, CancellationToken cancellationToken);

    /// <summary>
    /// Livreurs dont la position RECENTE tombe dans le rayon, du plus proche
    /// au plus lointain.
    ///
    /// « Recente » n'est pas decoratif : un telephone eteint laisse sa derniere
    /// position dans Redis indefiniment. Sans filtre de fraicheur, une vague
    /// entiere partirait vers des livreurs rentres chez eux depuis une heure,
    /// et le client attendrait trente secondes pour rien.
    /// </summary>
    Task<IReadOnlyList<LocatedDriver>> SearchAsync(
        GeoPoint center,
        int radiusMeters,
        int limit,
        CancellationToken cancellationToken);

    /// <summary>
    /// Toutes les positions RECENTES, de la plus fraiche a la plus ancienne.
    ///
    /// CE N'EST PAS SearchAsync SANS CENTRE. La recherche de proximite part
    /// de l'index geographique et filtre ensuite sur la fraicheur ; ici on
    /// part de la fraicheur — les seuls livreurs qui comptent sont ceux qui
    /// ont donne signe de vie — et on va chercher leurs coordonnees ensuite.
    /// L'ordre des deux operations n'est pas un detail : l'index GEO garde
    /// indefiniment la derniere position d'un telephone eteint, et balayer
    /// l'index reviendrait a peindre sur la carte des livreurs rentres chez
    /// eux la veille.
    ///
    /// SERT A REGARDER, PAS A REPARTIR. Aucune decision d'affectation ne se
    /// prend ici : le dispatch passe par SearchAsync, qui croise la base.
    /// </summary>
    Task<IReadOnlyList<DriverPosition>> ListFreshAsync(int limit, CancellationToken cancellationToken);

    /// <summary>
    /// Position RECENTE d'un livreur nomme, et sa distance a un point donne.
    /// Rend null si ce livreur n'a pas donne signe de vie dans la fenetre de
    /// fraicheur — ce qui est la reponse utile : on ne propose pas une course
    /// a un telephone eteint.
    ///
    /// CE N'EST PAS SearchAsync AVEC UN RAYON INFINI. La recherche de
    /// proximite repond « les N plus proches » ; il n'y a aucun rayon, ni
    /// aucune limite, qui garantisse qu'un livreur precis figure dans sa
    /// reponse. La question « celui-la, ou est-il ? » demande sa propre
    /// lecture.
    /// </summary>
    Task<MeasuredPosition?> MeasureAsync(
        Guid driverId,
        GeoPoint reference,
        CancellationToken cancellationToken);

    /// <summary>
    /// Fenetre de fraicheur, en secondes.
    ///
    /// ELLE SORT DU PORT PARCE QUE L'ECRAN DOIT LA DIRE. « 14 livreurs sur la
    /// carte » sans preciser « vus dans les deux dernieres minutes » se lit
    /// comme « 14 livreurs en service », ce qui n'est pas la meme chose. Le
    /// reglage vit dans l'infrastructure ; sa valeur appartient a la reponse.
    /// </summary>
    int FreshnessSeconds { get; }
}

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
