using System.ComponentModel.DataAnnotations;

namespace Hba.BuildingBlocks.Storage;

/// <summary>
/// Accès au stockage objet (ADR 0021 — Garage).
/// </summary>
public sealed class ObjectStoreOptions
{
    public const string SectionName = "ObjectStore";

    /// <summary>Clé du client réservé à la signature des URL de lecture.</summary>
    public const string PresigningClientKey = "objectstore-presign";

    /// <summary>Hôte et port, sans schéma : « garage:3900 ».</summary>
    [Required(AllowEmptyStrings = false)]
    public string Endpoint { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    public string Region { get; set; } = "hba";

    [Required(AllowEmptyStrings = false)]
    public string Bucket { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    public string AccessKey { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    public string SecretKey { get; set; } = string.Empty;

    /// <summary>
    /// Faux à l'intérieur du réseau Docker, vrai derrière un proxy TLS.
    /// </summary>
    public bool UseSsl { get; set; }

    /// <summary>
    /// Hôte et port VUS DEPUIS LE CLIENT : navigateur de la console,
    /// téléphone du livreur. Vide, <see cref="Endpoint"/> sert aux deux.
    ///
    /// POURQUOI DEUX ADRESSES POUR LE MEME STOCKAGE. Le service parle à
    /// Garage par son nom de réseau Docker, « garage:3900 », et c'est le bon
    /// nom pour écrire. Mais une URL signée est faite pour être suivie par
    /// quelqu'un d'autre, et personne hors du réseau Docker ne résout ce
    /// nom : le navigateur rendait ERR_NAME_NOT_RESOLVED sur chaque vignette.
    ///
    /// ET ON NE PEUT PAS RÉÉCRIRE L'HOTE APRES COUP : la signature SigV4
    /// couvre l'en-tête Host. Changer l'hôte d'une URL déjà signée la rend
    /// invalide. L'adresse publique doit donc être connue AU MOMENT de
    /// signer — d'où ce réglage plutôt qu'un remplacement de chaîne.
    ///
    /// En développement : « 192.168.1.24:3900 » plutôt que « localhost:3900 »,
    /// parce que la même URL doit marcher sur le Mac ET sur le téléphone.
    /// En production : le nom d'hôte de la route Traefik du stockage.
    /// </summary>
    public string PublicEndpoint { get; set; } = string.Empty;

    /// <summary>
    /// TLS de <see cref="PublicEndpoint"/>. Sans effet tant que celui-ci est
    /// vide : la signature reprend alors <see cref="UseSsl"/>, comme avant.
    /// </summary>
    public bool PublicUseSsl { get; set; }

    /// <summary>Hôte contre lequel les URL de lecture sont signées.</summary>
    public string SigningEndpoint =>
        string.IsNullOrWhiteSpace(PublicEndpoint) ? Endpoint : PublicEndpoint;

    /// <summary>TLS de l'hôte de signature.</summary>
    public bool SigningUseSsl =>
        string.IsNullOrWhiteSpace(PublicEndpoint) ? UseSsl : PublicUseSsl;

    /// <summary>
    /// Durée de vie des URL signées, en secondes.
    ///
    /// COURTE PAR CONSTRUCTION. Une URL de pièce d'identité qui traîne dans un
    /// historique de navigateur ou un journal de proxy est une fuite en
    /// différé : cinq minutes suffisent à afficher une image, pas à la
    /// partager.
    /// </summary>
    [Range(30, 3600)]
    public int UrlTtlSeconds { get; set; } = 300;
}
