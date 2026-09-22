using Hba.Identity.Application.Ports;
using Hba.Identity.Domain.Accounts;
using Hba.Identity.Domain.Partners;
using Hba.Identity.Domain.Sessions;
using Hba.Identity.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace Hba.Identity.Infrastructure.Persistence;

internal sealed class AccountRepository(IdentityDbContext context) : IAccountRepository
{
    public Task<Account?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
        => context.Accounts.FirstOrDefaultAsync(a => a.Id == id, cancellationToken);

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

    public void Add(Account account) => context.Accounts.Add(account);
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

    public void Add(PartnerClient client) => context.PartnerClients.Add(client);
}
