namespace Hba.Realtime.Api.Options;

public sealed class RealtimeOptions
{
    public const string SectionName = "Realtime";

    /// <summary>
    /// Canal Redis sur lequel Location publie les battements de position.
    /// </summary>
    ///
    /// <remarks>
    /// LE NOM EST PARTAGE AVEC LOCATION, ET C'EST LE SEUL COUPLAGE ENTRE LES DEUX
    /// SERVICES. Ils ne s'appellent jamais : l'un publie, l'autre ecoute. Si les deux
    /// noms divergent, rien ne casse et rien n'arrive — la carte reste figee sans la
    /// moindre erreur. D'ou la valeur par defaut identique des deux cotes, et ce
    /// commentaire dans les deux fichiers.
    /// </remarks>
    public string PositionChannel { get; set; } = "hba.location.positions";

    /// <summary>
    /// Duree de vie de l'association mission → chauffeur dans Redis.
    /// </summary>
    ///
    /// <remarks>
    /// POURQUOI UNE EXPIRATION PLUTOT QU'UNE SUPPRESSION SEULE. L'association est
    /// effacee quand la mission se clot — c'est le chemin normal. Mais une mission
    /// dont l'evenement de cloture serait perdu laisserait une entree eternelle, et
    /// avec elle le droit de diffuser la position d'un chauffeur a un client dont la
    /// course est finie. L'expiration est le garde-fou : au pire, la fuite dure
    /// douze heures au lieu de toujours.
    ///
    /// DOUZE HEURES, ET NON UNE. Une course dure vingt minutes, mais une livraison
    /// programmee peut attendre, et un chauffeur peut etre affecte longtemps avant de
    /// partir. Trop court, l'association disparait sous les pieds d'une mission en
    /// cours et le client perd le point qui bouge sans explication.
    /// </remarks>
    public int MissionDriverTtlHours { get; set; } = 12;
}
