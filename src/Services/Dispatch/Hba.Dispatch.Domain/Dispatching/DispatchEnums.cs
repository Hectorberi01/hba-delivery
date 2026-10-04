namespace Hba.Dispatch.Domain.Dispatching;

/// <summary>Etat d'une offre. Les valeurs suivent le contrat gRPC.</summary>
public enum OfferStatus
{
    Unspecified = 0,
    Pending = 1,
    Accepted = 2,
    Declined = 3,
    Expired = 4,

    /// <summary>Une autre offre de la meme vague a ete acceptee.</summary>
    Superseded = 5,
}

/// <summary>
/// Etat de la recherche pour une livraison. Il n'est pas dans le contrat : la
/// machine a etats publique est celle de la livraison, et Dispatch n'en est
/// qu'un moteur.
/// </summary>
public enum DispatchStatus
{
    Unspecified = 0,

    /// <summary>Des vagues sont en cours ou a venir.</summary>
    Searching = 1,

    Assigned = 2,

    /// <summary>Toutes les vagues ont echoue.</summary>
    Exhausted = 3,

    /// <summary>La course a ete annulee ailleurs : plus aucune offre ne part.</summary>
    Cancelled = 4,
}

/// <summary>
/// La nature de la mission cherchee.
/// </summary>
///
/// <remarks>
/// POURQUOI CE TYPE EXISTE. Le moteur portait « DeliveryId » en dur : il ne
/// savait chercher que pour un colis. Depuis le 1er octobre 2026 il cherche
/// aussi pour une course de personnes, et ce ne sont pas les memes chauffeurs —
/// le transport de passagers demande une habilitation declaree et un vehicule
/// capable d'en porter.
///
/// IL DECIDE DE L'HABILITATION EXIGEE, et c'est son seul effet metier dans ce
/// service. Voir <see cref="DispatchAggregate.CapaciteExigee"/>.
///
/// « NON PRECISE » EST REFUSE, PAS INTERPRETE COMME UN COLIS. Une recherche sans
/// nature ne sait ni quelle habilitation exiger ni a quel service rendre compte.
/// Les lignes anterieures au 1er octobre 2026 ont ete reprises explicitement en
/// Delivery par migration, pas laissees a zero.
///
/// LES NUMEROS SUIVENT LE CONTRAT hba.common.v1.ServiceType et ne se
/// reordonnent pas : ils sont persistes.
/// </remarks>
public enum ServiceType
{
    Unspecified = 0,

    /// <summary>Un colis : HBA Express, HBA Food, ou un partenaire B2B.</summary>
    Delivery = 1,

    /// <summary>Une course de personnes.</summary>
    Ride = 2,
}

/// <summary>
/// Ce qu'un chauffeur doit etre habilite a faire pour recevoir cette mission.
/// Les numeros suivent hba.common.v1.DriverCapability.
/// </summary>
public enum DriverCapability
{
    Unspecified = 0,
    PackageDelivery = 1,
    PassengerTransport = 2,
    FoodDelivery = 3,
}

/// <summary>
/// Type de vehicule exige par une mission. Les numeros suivent
/// hba.common.v1.VehicleType et ne se reordonnent pas.
/// </summary>
///
/// <remarks>
/// DISPATCH N'EN FAIT RIEN LUI-MEME : il le transmet a Driver, qui sait quels
/// chauffeurs roulent en quoi. Le garder ici evite qu'il faille interroger la
/// livraison ou la course a chaque vague pour une valeur figee a l'ouverture.
/// </remarks>
public enum VehicleType
{
    Unspecified = 0,
    Motorcycle = 1,
    Car = 2,
    Van = 3,
    Bicycle = 4,
    Tricycle = 5,
}
