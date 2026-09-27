using System.Security.Cryptography;
using System.Text;
using Hba.Identity.Application.Common.Interfaces;
using Microsoft.Extensions.Options;

namespace Hba.Identity.Infrastructure.Services.Security;

public sealed class ServiceClientOptions
{
    public const string SectionName = "ServiceClients";

    /// <summary>
    /// Nom du service vers son secret. Renseigné par l'hôte :
    /// ServiceClients__Clients__delivery=…
    /// </summary>
    public Dictionary<string, string> Clients { get; } = new(StringComparer.Ordinal);
}

/// <summary>
/// Vérifie les identifiants d'un service contre la configuration de l'hôte.
/// </summary>
public sealed class ConfiguredServiceClientRegistry(IOptions<ServiceClientOptions> options)
    : IServiceClientRegistry
{
    private readonly ServiceClientOptions _options = options.Value;

    public bool Verify(string? clientId, string? clientSecret)
    {
        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
        {
            return false;
        }

        if (!_options.Clients.TryGetValue(clientId, out var expected) || string.IsNullOrWhiteSpace(expected))
        {
            // On compare quand même, contre une valeur qui ne peut pas
            // correspondre : un service inconnu doit coûter le même temps
            // qu'un mauvais secret, sinon la durée de réponse dit lesquels
            // existent.
            expected = "\0";
        }

        // Comparaison à temps constant sur les empreintes plutôt que sur les
        // secrets : deux empreintes font toujours 32 octets, donc la seule
        // longueur du secret ne fuit pas non plus.
        var left = SHA256.HashData(Encoding.UTF8.GetBytes(clientSecret));
        var right = SHA256.HashData(Encoding.UTF8.GetBytes(expected));

        return CryptographicOperations.FixedTimeEquals(left, right);
    }
}
