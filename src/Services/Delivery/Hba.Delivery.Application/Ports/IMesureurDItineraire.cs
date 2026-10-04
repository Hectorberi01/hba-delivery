namespace Hba.Delivery.Application.Ports;

/// <summary>
/// Le trajet qui reste au livreur, et dans combien de temps il arrive.
/// </summary>
///
/// <remarks>
/// IL EST CALCULÉ À LA LECTURE, IL N'EST PAS STOCKÉ. Une estimation vieillit à
/// la seconde : la figer en base obligerait à la rafraîchir en permanence pour
/// une valeur que personne ne relira jamais. Delivery prend la dernière position
/// connue du livreur et demande la durée à Routing, qui la met en cache.
///
/// DELIVERY NE SAIT PAS QUI MESURE. Ni OSRM, ni Google : c'est Routing qui
/// connaît un fournisseur d'itinéraires, et lui seul (voir son contrat). Ce port
/// est ce qui garde cette ignorance — en changer reviendrait à rouvrir la seule
/// décision que ce découpage protège.
/// </remarks>
public interface IMesureurDItineraire
{
    /// <summary>
    /// Mesure le trajet restant entre la dernière position connue d'un livreur et
    /// un point.
    /// </summary>
    ///
    /// <param name="avecTrace">
    /// Demande la géométrie de l'itinéraire. ELLE COÛTE : plusieurs kilo-octets
    /// par réponse, pour un tracé que seul un écran de carte dessine. L'appelant
    /// ne la demande que lorsqu'il va la peindre.
    /// </param>
    ///
    /// <remarks>
    /// NUL QUAND ON NE SAIT PAS, ET CE N'EST PAS UN CAS RARE : aucune position
    /// encore remontée, position périmée, Routing injoignable. Rendre zéro aurait
    /// dit « il est arrivé », ce qui est exactement le contraire.
    /// </remarks>
    Task<RouteActive?> MesurerAsync(
        string driverId,
        double cibleLatitude,
        double cibleLongitude,
        string profil,
        bool avecTrace,
        CancellationToken cancellationToken);
}

/// <summary>Le trajet restant, et d'où vient la mesure.</summary>
///
/// <param name="DistanceMetres">Distance restante par la route.</param>
/// <param name="Secondes">Durée estimée du trajet restant.</param>
/// <param name="DuRepli">
/// Vrai quand la mesure vient du repli à vol d'oiseau de Routing. La durée est
/// alors SOUS-ESTIMÉE d'environ un tiers en ville : l'appelant doit en faire
/// quelque chose plutôt que de l'afficher telle quelle (point 43).
/// </param>
/// <param name="CalculeeA">Quand la mesure a été faite.</param>
/// <param name="Trace">
/// Tracé encodé au format polyline Google, vide si la géométrie n'a pas été
/// demandée — ou si le repli a répondu, qui ne dessine rien.
/// </param>
public sealed record RouteActive(
    int DistanceMetres,
    int Secondes,
    bool DuRepli,
    DateTimeOffset CalculeeA,
    string Trace);
