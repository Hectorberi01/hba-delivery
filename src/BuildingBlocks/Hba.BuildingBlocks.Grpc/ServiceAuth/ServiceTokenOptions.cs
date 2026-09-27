namespace Hba.BuildingBlocks.Grpc.ServiceAuth;

/// <summary>
/// Identité du service auprès d'Identity, pour les appels qu'aucun utilisateur
/// n'a déclenchés.
/// </summary>
public sealed class ServiceTokenOptions
{
    public const string SectionName = "ServiceToken";

    /// <summary>Adresse gRPC d'Identity. PORT 8081, pas 8080 : 8080 est en Http1.</summary>
    public string IdentityAddress { get; set; } = "http://identity:8081";

    /// <summary>Nom du service appelant : « delivery », « dispatch »…</summary>
    public string ClientId { get; set; } = string.Empty;

    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>
    /// Marge de renouvellement. Le jeton est redemandé ce nombre de secondes
    /// avant son expiration, pour qu'un appel ne parte jamais avec un jeton
    /// périmé pendant son vol.
    /// </summary>
    public int RenewBeforeSeconds { get; set; } = 120;

    /// <summary>Délai de l'appel à Identity. Court : c'est un aller-retour local.</summary>
    public int RequestTimeoutSeconds { get; set; } = 10;
}
