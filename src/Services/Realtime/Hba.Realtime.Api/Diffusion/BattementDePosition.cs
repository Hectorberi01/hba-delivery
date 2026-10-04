using System.Text.Json.Serialization;

namespace Hba.Realtime.Api.Diffusion;

/// <summary>
/// Ce que Location publie sur le canal Redis, a chaque battement.
/// </summary>
///
/// <remarks>
/// CETTE FORME EST ECRITE DEUX FOIS, DANS DEUX SERVICES, ET C'EST ASSUME.
/// L'original est <c>Hba.Location.Infrastructure.Services.BattementPublie</c> ; cette
/// copie est le lecteur. Les deux services ne partagent aucun projet — un contrat
/// partage pour un canal interne aurait demande un protobuf, donc une place dans
/// contracts/, donc un contrat PUBLIC pour un detail de transport entre deux
/// processus.
///
/// CE QUE LA DUPLICATION COUTE, ET COMMENT ON S'EN PROTEGE. Si un champ change d'un
/// cote seulement, la deserialisation ne LEVE PAS : System.Text.Json ignore ce qu'il
/// ne connait pas et laisse a zero ce qui manque. Une carte afficherait alors des
/// points a la latitude zero, au large de l'Afrique — visible, mais incomprehensible.
/// D'ou la verification explicite dans <see cref="DiffuseurDePositions"/> : un
/// battement sans identifiant ou sans coordonnees est ignore, pas affiche.
///
/// LES NOMS SONT COURTS PARCE QUE C'EST LE MESSAGE LE PLUS FREQUENT DU SYSTEME. Un
/// battement par chauffeur en ligne toutes les vingt secondes, et chaque octet
/// traverse Redis et toutes les repliques. « d » plutot que « driverId » economise
/// peu ; l'ecrire une fois pour toutes dans ces deux fichiers ne coute rien.
/// </remarks>
internal sealed record BattementDePosition
{
    [JsonPropertyName("d")]
    public string DriverId { get; init; } = string.Empty;

    [JsonPropertyName("la")]
    public double Latitude { get; init; }

    [JsonPropertyName("lo")]
    public double Longitude { get; init; }

    /// <summary>Cap en degres. Absent quand le telephone ne l'a pas donne.</summary>
    [JsonPropertyName("c")]
    public double? CapDegres { get; init; }

    [JsonPropertyName("v")]
    public double? VitesseMps { get; init; }

    [JsonPropertyName("p")]
    public double? PrecisionMetres { get; init; }

    /// <summary>Heure de RECEPTION par Location, en secondes Unix.</summary>
    ///
    /// <remarks>
    /// EN SECONDES UNIX ET NON EN ISO 8601 : c'est le format que Location a deja sous la
    /// main — c'est le score de son ensemble trie — et une chaine de vingt-cinq
    /// caracteres par battement n'apporterait rien a un message que seul du code lit.
    /// </remarks>
    [JsonPropertyName("t")]
    public long VuAUnix { get; init; }
}
