using Hba.Identity.Domain.Accounts;

namespace Hba.Identity.Domain.Interfaces;

/// <summary>
/// Accès aux comptes. L'interface est dans le Domaine, son implémentation dans
/// l'Infrastructure : le Domaine dit ce dont il a besoin, il n'apprend jamais
/// avec quoi c'est rendu.
///
/// AUCUNE METHODE NE RENVOIE IQueryable. Exposer une requête composable
/// laisserait l'appelant écrire du SQL par accident et ferait remonter EF Core
/// jusqu'ici ; chaque question posée aux comptes porte donc un nom.
/// </summary>
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

    /// <summary>
    /// Les comptes dont l'échéance d'effacement est atteinte.
    /// </summary>
    ///
    /// <remarks>
    /// LE PLAFOND EST DANS LA SIGNATURE, ET NON LAISSE A L'APPELANT. Un
    /// effacement fait travailler Directory et Media par événement ; après un
    /// arrêt prolongé, tout prendre d'un coup saturerait le topic. Le passage
    /// suivant reprend la suite.
    /// </remarks>
    Task<IReadOnlyList<Account>> ListDeletionsDueAsync(
        DateTimeOffset now,
        int limit,
        CancellationToken cancellationToken);

    void Add(Account account);

    /// <summary>
    /// Retire la ligne pour de bon.
    /// </summary>
    ///
    /// <remarks>
    /// LA SEULE SUPPRESSION DE TOUT IDENTITY, et elle n'est appelée que par le
    /// travail d'effacement, après que le titulaire l'a demandée et que le délai
    /// de grâce a couru. L'administration ne supprime pas : elle suspend.
    /// </remarks>
    void Remove(Account account);
}
