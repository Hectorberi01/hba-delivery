namespace Hba.Location.Infrastructure.Options;

public sealed class LocationOptions
{
    public const string SectionName = "Location";

    /// <summary>
    /// Fenetre au-dela de laquelle une position ne compte plus, en secondes.
    /// </summary>
    ///
    /// <remarks>
    /// ELLE EXISTE PARCE QUE REDIS GEO NE SAIT PAS FAIRE EXPIRER UN MEMBRE. La
    /// derniere position d'un telephone eteint resterait indefiniment dans l'index :
    /// sans cette fenetre, une vague entiere partirait vers des chauffeurs rentres
    /// chez eux depuis une heure, et le client attendrait trente secondes pour rien.
    ///
    /// DEUX MINUTES PAR DEFAUT, et le reglage est un compromis entre deux defauts
    /// symetriques. Trop court, un chauffeur qui traverse un tunnel ou un quartier mal
    /// couvert disparait de la carte alors qu'il travaille. Trop long, on propose des
    /// courses a des telephones eteints. La valeur suit la cadence du battement de
    /// l'application chauffeur : elle doit en couvrir plusieurs, pour qu'un seul point
    /// perdu ne fasse pas disparaitre quelqu'un.
    /// </remarks>
    public int FreshnessSeconds { get; set; } = 120;

    /// <summary>
    /// Plafond par defaut de la carte de l'exploitation, quand l'appelant n'en donne
    /// aucun.
    /// </summary>
    public int DefaultListLimit { get; set; } = 200;

    /// <summary>
    /// Canal Redis sur lequel chaque battement est publie, pour le temps reel.
    /// </summary>
    ///
    /// <remarks>
    /// LE NOM EST PARTAGE AVEC Hba.Realtime, ET C'EST LE SEUL COUPLAGE ENTRE LES DEUX
    /// SERVICES. Ils ne s'appellent jamais : l'un publie, l'autre ecoute. Si les deux
    /// noms divergent, RIEN NE CASSE ET RIEN N'ARRIVE — la carte du client reste figee
    /// sans la moindre erreur, dans aucun journal. D'ou la valeur par defaut identique
    /// des deux cotes, et ce commentaire dans les deux fichiers.
    ///
    /// VIDE, LA PUBLICATION EST DESACTIVEE. L'index continue d'etre ecrit, les
    /// recherches continuent de fonctionner : seul le temps reel s'eteint. C'est voulu —
    /// Location doit pouvoir tourner sans que le service de diffusion existe, et c'est
    /// d'ailleurs ce qu'il a fait pendant toute la phase 9.
    /// </remarks>
    public string PositionChannel { get; set; } = "hba.location.positions";
}
