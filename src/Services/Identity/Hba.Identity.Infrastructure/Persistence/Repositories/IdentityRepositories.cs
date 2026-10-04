using Hba.Identity.Application.Common.Interfaces;
using Hba.Identity.Domain;
using Hba.Identity.Domain.Accounts;
using Hba.Identity.Domain.Interfaces;
using Hba.Identity.Domain.Partners;
using Hba.Identity.Domain.Sessions;
using Hba.Identity.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace Hba.Identity.Infrastructure.Persistence.Repositories;

internal sealed class AccountRepository(IdentityDbContext context) : IAccountRepository
{
    public Task<Account?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
        => context.Accounts.FirstOrDefaultAsync(a => a.Id == id, cancellationToken);

    /// <summary>
    /// ON FILTRE SUR LE STATUT *ET* SUR LA DATE, alors que l'un devrait suffire.
    /// Les deux champs bougent ensemble dans le domaine ; s'ils cessaient de le
    /// faire — une migration maladroite, une écriture directe en base — la
    /// requête la plus laxiste des deux effacerait des comptes qui n'ont rien
    /// demandé. Ici, il faut que les deux soient d'accord.
    ///
    /// L'ORDRE EST CELUI DES ECHEANCES : sous le plafond, le plus ancien dû
    /// part d'abord, et personne n'attend indéfiniment derrière un afflux.
    /// </summary>
    public async Task<IReadOnlyList<Account>> ListDeletionsDueAsync(
        DateTimeOffset now,
        int limit,
        CancellationToken cancellationToken)
        => await context.Accounts
            .Where(a => a.Status == AccountStatus.PendingDeletion
                        && a.DeletionScheduledFor != null
                        && a.DeletionScheduledFor <= now)
            .OrderBy(a => a.DeletionScheduledFor)
            .Take(limit < 1 ? 1 : limit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public Task<Account?> GetByPhoneAsync(string phone, CancellationToken cancellationToken)
    {
        if (!PhoneNumber.IsValid(phone))
        {
            return Task.FromResult<Account?>(null);
        }

        var value = PhoneNumber.Create(phone);
        return context.Accounts.FirstOrDefaultAsync(a => a.Phone == value, cancellationToken);
    }

    public Task<Account?> GetByEmailAsync(string email, CancellationToken cancellationToken)
    {
        if (!EmailAddress.IsValid(email))
        {
            return Task.FromResult<Account?>(null);
        }

        var value = EmailAddress.Create(email);
        return context.Accounts.FirstOrDefaultAsync(a => a.Email == value, cancellationToken);
    }

    /// <summary>
    /// L'utilisateur du portail ne doit pas avoir à se souvenir s'il s'est
    /// inscrit avec son e-mail ou son numéro : on essaie les deux.
    /// </summary>
    public async Task<Account?> GetByLoginAsync(string login, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(login))
        {
            return null;
        }

        if (EmailAddress.IsValid(login))
        {
            return await GetByEmailAsync(login, cancellationToken).ConfigureAwait(false);
        }

        return await GetByPhoneAsync(login, cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> PhoneExistsAsync(string phone, CancellationToken cancellationToken)
    {
        if (!PhoneNumber.IsValid(phone))
        {
            return false;
        }

        var value = PhoneNumber.Create(phone);
        return await context.Accounts.AnyAsync(a => a.Phone == value, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Les rôles sont persistés en une colonne « a,b,c ». On encadre la valeur
    /// et le motif de virgules pour ne pas confondre un rôle avec le préfixe
    /// d'un autre.
    /// </summary>
    public Task<bool> AnyWithRoleAsync(string role, CancellationToken cancellationToken)
        => context.Accounts.AnyAsync(
            a => EF.Functions.Like("," + EF.Property<string>(a, "RolesRaw") + ",", "%," + role + ",%"),
            cancellationToken);

    /// <summary>
    /// QUATRE TERMES PLUTOT QU'UNE BOUCLE, PARCE QUE LA REQUETE DOIT PARTIR EN
    /// SQL. Une liste de rôles parcourue en C# obligerait à charger la table
    /// pour la filtrer en mémoire ; écrits à la main, les quatre « LIKE »
    /// partent au serveur.
    ///
    /// LES VIRGULES ENCADRANTES NE SONT PAS DECORATIVES : sans elles, « ops »
    /// trouverait un hypothétique « devops », et « admin » un « sysadmin ».
    /// Même procédé que AnyWithRoleAsync.
    ///
    /// SI LE REFERENTIEL AJOUTE UN ROLE DE BACK-OFFICE, IL EN FAUT UN
    /// CINQUIEME ICI. Le compilateur ne le dira pas : Roles.BackOffice est un
    /// ensemble, et cette requête en est une copie figée, faute de pouvoir
    /// traduire l'ensemble lui-même.
    /// </summary>
    public async Task<IReadOnlyList<Account>> ListBackOfficeAsync(int limit, CancellationToken cancellationToken)
        => await context.Accounts
            .Where(a =>
                EF.Functions.Like("," + EF.Property<string>(a, "RolesRaw") + ",", "%," + Roles.Admin + ",%")
                || EF.Functions.Like("," + EF.Property<string>(a, "RolesRaw") + ",", "%," + Roles.Ops + ",%")
                || EF.Functions.Like("," + EF.Property<string>(a, "RolesRaw") + ",", "%," + Roles.Support + ",%")
                || EF.Functions.Like("," + EF.Property<string>(a, "RolesRaw") + ",", "%," + Roles.Finance + ",%"))
            .OrderBy(a => a.DisplayName)
            .Take(limit < 1 ? 1 : limit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public void Add(Account account) => context.Accounts.Add(account);

    public void Remove(Account account) => context.Accounts.Remove(account);
}

internal sealed class RefreshTokenRepository(IdentityDbContext context) : IRefreshTokenRepository
{
    public Task<RefreshToken?> GetByHashAsync(string tokenHash, CancellationToken cancellationToken)
        => context.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == tokenHash, cancellationToken);

    public async Task<IReadOnlyList<RefreshToken>> ListBySessionAsync(
        Guid sessionId,
        CancellationToken cancellationToken)
        => await context.RefreshTokens
            .Where(t => t.SessionId == sessionId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public async Task<IReadOnlyList<RefreshToken>> ListActiveByAccountAsync(
        Guid accountId,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;

        return await context.RefreshTokens
            .Where(t => t.AccountId == accountId && t.RevokedAt == null && t.ExpiresAt > now)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public void Add(RefreshToken token) => context.RefreshTokens.Add(token);
}

internal sealed class PartnerClientRepository(IdentityDbContext context) : IPartnerClientRepository
{
    public Task<PartnerClient?> GetByIdAsync(Guid partnerId, CancellationToken cancellationToken)
        => context.PartnerClients.FirstOrDefaultAsync(p => p.Id == partnerId, cancellationToken);

    public Task<PartnerClient?> GetByClientIdAsync(string clientId, CancellationToken cancellationToken)
        => context.PartnerClients.FirstOrDefaultAsync(p => p.ClientId == clientId, cancellationToken);

    public async Task<IReadOnlyList<PartnerClient>> ListAsync(int limit, CancellationToken cancellationToken)
        => await context.PartnerClients
            .AsNoTracking()
            .OrderBy(p => p.Name)
            .Take(limit < 1 ? 1 : limit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public void Add(PartnerClient client) => context.PartnerClients.Add(client);
}
