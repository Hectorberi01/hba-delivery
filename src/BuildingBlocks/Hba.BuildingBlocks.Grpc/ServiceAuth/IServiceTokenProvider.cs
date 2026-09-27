using Grpc.Core;

namespace Hba.BuildingBlocks.Grpc.ServiceAuth;

/// <summary>
/// Jeton d'accès du service lui-même, mis en cache et renouvelé avant terme.
///
/// A N'UTILISER QUE LORSQU'IL N'Y A PAS D'UTILISATEUR. Un appel né d'une
/// requête HTTP reporte le jeton de l'appelant : c'est ce que fait
/// <see cref="Interceptors.TokenForwardingInterceptor"/>, et c'est ce qui
/// permet au service appelé d'appliquer ses propres règles. Le jeton de
/// service ne porte aucun rôle ; il ne remplace donc jamais celui d'un
/// utilisateur, il couvre seulement les appels partis d'un consommateur Kafka
/// ou d'un planificateur.
/// </summary>
public interface IServiceTokenProvider
{
    /// <summary>Jeton courant, redemandé à Identity s'il est expiré ou proche de l'être.</summary>
    ValueTask<string> GetTokenAsync(CancellationToken cancellationToken);

    /// <summary>
    /// En-têtes gRPC prêts à poser sur un appel sortant. VIDES si l'appel
    /// descend d'une requête utilisateur : dans ce cas c'est le jeton de
    /// l'utilisateur qui doit partir, et l'intercepteur s'en charge.
    /// </summary>
    ValueTask<Metadata> AuthorizationAsync(CancellationToken cancellationToken);
}
