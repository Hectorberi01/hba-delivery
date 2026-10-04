namespace Hba.Routing.Infrastructure.Options;

/// <summary>Réglages du service Routing.</summary>
///
/// <remarks>
/// PAS D'ANNOTATIONS DE VALIDATION, ET CE N'EST PAS UN OUBLI.
/// <c>ValidateDataAnnotations</c> vient du paquet
/// <c>Microsoft.Extensions.Options.DataAnnotations</c>, que ce dépôt ne déclare
/// nulle part — <c>BuildingBlocks.Storage</c> ne l'obtient que par transitivité
/// de Minio, donc par accident. Les bornes sont vérifiées explicitement dans
/// <c>AddRoutingInfrastructure</c>, ce qui est la convention du dépôt et donne
/// de meilleurs messages qu'un attribut.
/// </remarks>
public sealed class RoutingOptions
{
    public const string SectionName = "Routing";

    /// <summary>
    /// Le fournisseur choisi : « osrm » ou « haversine ».
    /// </summary>
    ///
    /// <remarks>
    /// « haversine » N'EST PAS UN MODE DÉGRADÉ HONTEUX, c'est un mode de
    /// développement assumé : préparer la carte du Bénin pour OSRM demande un
    /// téléchargement de plusieurs centaines de mégaoctets et trois passes de
    /// pré-traitement. Tant que ce n'est pas fait, le service répond quand même,
    /// et chaque mesure porte <c>FromFallback</c>.
    /// </remarks>
    public string Provider { get; set; } = "haversine";

    /// <summary>Adresse d'OSRM, par profil de circulation.</summary>
    ///
    /// <remarks>
    /// UN DICTIONNAIRE, ET NON UNE SEULE ADRESSE, POUR UNE RAISON QU'IL FAUT
    /// CONNAÎTRE : le profil d'OSRM est figé AU PRÉ-TRAITEMENT de la carte, pas
    /// à l'appel. Le segment de profil dans l'URL — « /route/v1/driving/… » —
    /// est décoratif : une instance préparée avec le profil voiture répondra
    /// toujours en voiture, même interrogée en « bike ».
    ///
    /// CONSÉQUENCE CONCRÈTE : servir vraiment trois profils demande TROIS
    /// instances d'OSRM, chacune avec sa carte pré-traitée. Tant qu'il n'y en a
    /// qu'une, tous les profils pointent dessus et rendent la même mesure — ce
    /// qui est acceptable pour la moto et la voiture, qui empruntent les mêmes
    /// voies, et faux pour le vélo.
    /// </remarks>
    public Dictionary<string, string> OsrmEndpoints { get; } = [];

    /// <summary>Adresse par défaut, utilisée pour tout profil sans adresse propre.</summary>
    public string OsrmBaseUrl { get; set; } = "http://osrm:5000";

    /// <summary>Délai au-delà duquel on replie. Court : un client attend.</summary>
    public int TimeoutSeconds { get; set; } = 5;

    /// <summary>Durée de vie d'un trajet en cache.</summary>
    ///
    /// <remarks>
    /// UNE HEURE PAR DÉFAUT. Un itinéraire ne change pas d'une minute à l'autre,
    /// mais un chantier ou une route coupée finissent par arriver — et ce qui
    /// est en cache ne le saura jamais.
    /// </remarks>
    public int CacheTtlSeconds { get; set; } = 3_600;
}
