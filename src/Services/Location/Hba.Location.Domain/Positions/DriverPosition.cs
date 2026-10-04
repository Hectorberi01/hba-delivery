namespace Hba.Location.Domain.Positions;

/// <summary>
/// La derniere position connue d'un chauffeur.
/// </summary>
///
/// <remarks>
/// IL N'Y A PAS D'AGREGAT DANS CE SERVICE, ET C'EST UN CONSTAT, PAS UN RACCOURCI.
/// Un agregat existe pour tenir des invariants dans le temps : une course ne passe
/// pas de « terminee » a « en cours », un paiement ne se capture pas deux fois. Une
/// position n'a aucun invariant de ce genre — elle est ECRASEE par la suivante, et
/// la precedente n'a aucune valeur. Inventer un agregat ici donnerait un objet dont
/// la seule methode serait « remplace-toi ».
///
/// DEUX HORODATAGES, ET IL FAUT LES DEUX. <see cref="CapturedAt"/> vient du
/// TELEPHONE : une horloge mal reglee, ou un point mis en file pendant une coupure
/// reseau, et il ne veut rien dire. <see cref="SeenAt"/> est l'heure de RECEPTION
/// par le service, et c'est elle, seule, qui decide de la fraicheur. Le premier
/// sert a comprendre apres coup pourquoi un point est arrive en retard ; le second
/// sert a decider maintenant.
/// </remarks>
public sealed record DriverPosition(
    Guid DriverId,
    GeoPoint Point,
    Motion Motion,
    DateTimeOffset CapturedAt,
    DateTimeOffset SeenAt);

/// <summary>
/// Un chauffeur trouve autour d'un point, avec sa distance a ce point.
/// </summary>
///
/// <remarks>
/// LA DISTANCE EST A VOL D'OISEAU, ET CE N'EST PAS UN ITINERAIRE. Elle sert a
/// ORDONNER des candidats, pas a annoncer un temps d'arrivee : a Cotonou, deux
/// points separes par la lagune sont proches a vol d'oiseau et loin par la route.
/// Le temps de trajet reel se demande a Routing, qui sait par ou passent les rues ;
/// ce service, lui, ne connait que des coordonnees.
/// </remarks>
public sealed record NearbyDriver(DriverPosition Position, int DistanceMeters);
