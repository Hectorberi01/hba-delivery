using Hba.Identity.Domain.Accounts;
using Hba.Identity.Domain.Partners;
using Hba.Identity.Domain.Sessions;

namespace Hba.Identity.Application.Ports;

public interface IAccountRepository
{
    Task<Account?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<Account?> GetByPhoneAsync(string phone, CancellationToken cancellationToken);

    Task<Account?> GetByEmailAsync(string email, CancellationToken cancellationToken);

    /// <summary>
    /// Recherche par e-mail OU téléphone, pour l'écran de connexion du portail :
    /// l'utilisateur ne doit pas avoir à se souvenir de ce qu'il a fourni.
    /// </summary>
    Task<Account?> GetByLoginAsync(string login, CancellationToken cancellationToken);

    Task<bool> PhoneExistsAsync(string phone, CancellationToken cancellationToken);

    /// <summary>
    /// Existe-t-il au moins un compte portant ce rôle ? Sert à l'amorçage : on
    /// ne crée le premier administrateur que s'il n'y en a aucun.
    /// </summary>
    Task<bool> AnyWithRoleAsync(string role, CancellationToken cancellationToken);

    void Add(Account account);
}

public interface IRefreshTokenRepository
{
    Task<RefreshToken?> GetByHashAsync(string tokenHash, CancellationToken cancellationToken);

    Task<IReadOnlyList<RefreshToken>> ListBySessionAsync(Guid sessionId, CancellationToken cancellationToken);

    Task<IReadOnlyList<RefreshToken>> ListActiveByAccountAsync(Guid accountId, CancellationToken cancellationToken);

    void Add(RefreshToken token);
}

public interface IPartnerClientRepository
{
    Task<PartnerClient?> GetByIdAsync(Guid partnerId, CancellationToken cancellationToken);

    Task<PartnerClient?> GetByClientIdAsync(string clientId, CancellationToken cancellationToken);

    void Add(PartnerClient client);
}
