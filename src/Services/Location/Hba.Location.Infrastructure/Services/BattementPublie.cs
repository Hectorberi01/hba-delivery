using System.Text.Json.Serialization;

namespace Hba.Location.Infrastructure.Services;

/// <summary>
/// Ce que Location publie sur le canal Redis, a chaque battement.
/// </summary>
///
/// <remarks>
/// LE LECTEUR EST DANS UN AUTRE SERVICE : <c>Hba.Realtime.Api.Diffusion
/// .BattementDePosition</c>. Cette forme est donc ECRITE DEUX FOIS, et c'est assume —
/// les deux services ne partagent aucun projet, et un contrat partage pour un canal
/// interne aurait demande un protobuf dans contracts/, c'est-a-dire un contrat PUBLIC
/// pour un detail de transport entre deux processus.
///
/// SI UN CHAMP CHANGE D'UN SEUL COTE, RIEN NE LEVE : System.Text.Json ignore ce qu'il
/// ne connait pas et laisse a zero ce qui manque. Le lecteur se protege donc
/// explicitement — un battement sans identifiant ou sans coordonnees y est ignore
/// plutot qu'affiche a la latitude zero. Changer un nom ici demande de changer l'autre
/// fichier dans le meme commit.
///
/// LES NOMS SONT COURTS PARCE QUE C'EST LE MESSAGE LE PLUS FREQUENT DU SYSTEME : un
/// battement par chauffeur en ligne toutes les vingt secondes, et chaque octet
/// traverse Redis et toutes les repliques du service de diffusion.
/// </remarks>
internal sealed record BattementPublie
{
    [JsonPropertyName("d")]
    public string DriverId { get; init; } = string.Empty;

    [JsonPropertyName("la")]
    public double Latitude { get; init; }

    [JsonPropertyName("lo")]
    public double Longitude { get; init; }

    [JsonPropertyName("c")]
    public double? CapDegres { get; init; }

    [JsonPropertyName("v")]
    public double? VitesseMps { get; init; }

    [JsonPropertyName("p")]
    public double? PrecisionMetres { get; init; }

    /// <summary>Heure de RECEPTION, en secondes Unix. C'est elle qui dit la fraicheur.</summary>
    [JsonPropertyName("t")]
    public long VuAUnix { get; init; }
}
