using Hba.Location.Domain.Positions;

namespace Hba.Location.Application.Common;

/// <summary>
/// Le magasin des positions.
/// </summary>
///
/// <remarks>
/// UNE SEULE IMPLEMENTATION, ET ELLE EST DANS REDIS. Ce service n'a pas de base
/// relationnelle : ecrire un point toutes les quelques secondes par chauffeur en
/// ligne dans Postgres couterait une transaction par battement, pour une donnee qui
/// sera ecrasee avant d'avoir servi deux fois.
///
/// LA FENETRE DE FRAICHEUR EST UNE PROPRIETE DU PORT, pas un detail d'Infrastructure.
/// Elle sort du service parce que l'ECRAN DOIT LA DIRE : « 14 chauffeurs sur la
/// carte » sans preciser « vus dans les deux dernieres minutes » se lit comme un
/// effectif, alors que ce n'est qu'un compte de points recents.
///
/// AUCUNE METHODE NE REND UNE POSITION PERIMEE. Ce n'est pas une commodite : l'index
/// geographique de Redis ne sait pas faire expirer un membre, donc la derniere
/// position d'un telephone eteint y reste indefiniment. Un port qui les rendrait
/// obligerait chaque appelant a refaire le filtre, et le premier a l'oublier
/// proposerait une course a quelqu'un rentre chez lui depuis une heure.
/// </remarks>
public interface IPositionStore
{
    /// <summary>Fenetre de fraicheur, en secondes.</summary>
    int FreshnessSeconds { get; }

    /// <summary>Pose la derniere position connue. Ecrase la precedente.</summary>
    Task UpsertAsync(DriverPosition position, CancellationToken cancellationToken);

    /// <summary>Oublie la position d'un chauffeur.</summary>
    Task ForgetAsync(Guid driverId, CancellationToken cancellationToken);

    /// <summary>
    /// Position RECENTE d'un chauffeur nomme, ou null s'il n'a pas donne signe de vie
    /// dans la fenetre.
    ///
    /// CE N'EST PAS <see cref="SearchAsync"/> AVEC UN RAYON INFINI. La recherche de
    /// proximite repond « les N plus proches » : il n'y a aucun rayon, ni aucune
    /// limite, qui garantisse qu'un chauffeur precis figure dans sa reponse. La
    /// question « celui-la, ou est-il ? » demande sa propre lecture.
    /// </summary>
    Task<DriverPosition?> FindAsync(Guid driverId, CancellationToken cancellationToken);

    /// <summary>
    /// Chauffeurs dont la position RECENTE tombe dans le rayon, du plus proche au plus
    /// lointain.
    /// </summary>
    Task<IReadOnlyList<NearbyDriver>> SearchAsync(
        GeoPoint center,
        int radiusMeters,
        int limit,
        CancellationToken cancellationToken);

    /// <summary>
    /// Toutes les positions RECENTES, de la plus fraiche a la plus ancienne.
    ///
    /// CE N'EST PAS <see cref="SearchAsync"/> SANS CENTRE. La recherche de proximite
    /// part de l'index geographique et filtre ensuite sur la fraicheur ; ici on part
    /// de la fraicheur — les seuls chauffeurs qui comptent sont ceux qui ont donne
    /// signe de vie — et on va chercher leurs coordonnees ensuite. L'ordre des deux
    /// operations n'est pas un detail : balayer l'index reviendrait a peindre sur la
    /// carte des chauffeurs rentres chez eux la veille.
    /// </summary>
    Task<IReadOnlyList<DriverPosition>> ListFreshAsync(int limit, CancellationToken cancellationToken);
}
