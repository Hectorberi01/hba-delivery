using Hba.Identity.Domain.Sessions;

namespace Hba.Identity.Domain.Interfaces;

/// <summary>
/// Accès aux jetons de rafraîchissement. LA RECHERCHE SE FAIT PAR EMPREINTE,
/// JAMAIS PAR VALEUR : le jeton en clair n'existe que le temps d'un aller-retour
/// vers le client, et il n'est stocké nulle part.
/// </summary>
public interface IRefreshTokenRepository
{
    Task<RefreshToken?> GetByHashAsync(string tokenHash, CancellationToken cancellationToken);

    Task<IReadOnlyList<RefreshToken>> ListBySessionAsync(Guid sessionId, CancellationToken cancellationToken);

    Task<IReadOnlyList<RefreshToken>> ListActiveByAccountAsync(Guid accountId, CancellationToken cancellationToken);

    void Add(RefreshToken token);
}
