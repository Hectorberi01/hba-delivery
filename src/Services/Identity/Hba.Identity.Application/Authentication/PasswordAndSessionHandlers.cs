using Hba.BuildingBlocks.Application.Abstractions;
using Hba.BuildingBlocks.Application.Messaging;
using Hba.BuildingBlocks.Domain;
using Hba.Identity.Application.Ports;
using Hba.Identity.Application.Services;
using Hba.Identity.Application.Views;
using Microsoft.Extensions.Logging;

namespace Hba.Identity.Application.Authentication;

public sealed class LoginWithPasswordHandler(
    IAccountRepository accounts,
    SessionIssuer sessions,
    IUnitOfWork unitOfWork,
    IClock clock) : ICommandHandler<LoginWithPasswordCommand, TokenPairView>
{
    public async Task<TokenPairView> HandleAsync(LoginWithPasswordCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var now = clock.UtcNow;
        var account = await accounts.GetByLoginAsync(command.Login, cancellationToken).ConfigureAwait(false);

        // Identifiant inconnu et mot de passe faux donnent la MÊME réponse :
        // rien ne doit permettre de savoir si un compte existe.
        if (account is null || !account.TryPassword(command.Password, now))
        {
            if (account is not null)
            {
                // Le compteur d'échecs vient de changer : il faut le persister.
                await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }

            throw new ForbiddenException("Identifiants incorrects.");
        }

        account.EnsureCanAuthenticate();
        account.RecordLogin(now);

        var pair = sessions.Issue(account, command.DeviceId, Guid.CreateVersion7(), now);

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return pair;
    }
}

public sealed class RefreshSessionHandler(
    IAccountRepository accounts,
    IRefreshTokenRepository refreshTokens,
    ITokenIssuer tokens,
    SessionIssuer sessions,
    IUnitOfWork unitOfWork,
    IClock clock,
    ILogger<RefreshSessionHandler> logger) : ICommandHandler<RefreshSessionCommand, TokenPairView>
{
    public async Task<TokenPairView> HandleAsync(RefreshSessionCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var now = clock.UtcNow;
        var hash = tokens.HashRefreshToken(command.RefreshToken ?? string.Empty);

        var presented = await refreshTokens.GetByHashAsync(hash, cancellationToken).ConfigureAwait(false)
            ?? throw new ForbiddenException("Jeton de rafraîchissement inconnu.");

        // Un jeton déjà consommé qui revient : soit un rejeu, soit un vol. Dans
        // le doute, toute la session tombe — l'utilisateur se reconnectera.
        if (presented.ConsumedAt is not null)
        {
            var chain = await refreshTokens
                .ListBySessionAsync(presented.SessionId, cancellationToken)
                .ConfigureAwait(false);

            foreach (var token in chain)
            {
                token.Revoke("Jeton de rafraîchissement rejoué.", now);
            }

            await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            logger.LogWarning(
                "Jeton de rafraîchissement rejoué pour la session {SessionId} : chaîne révoquée.",
                presented.SessionId);

            throw new ForbiddenException("Session révoquée. Reconnectez-vous.");
        }

        var account = await accounts.GetByIdAsync(presented.AccountId, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException("Compte", presented.AccountId.ToString());

        account.EnsureCanAuthenticate();

        var pair = sessions.Rotate(account, presented, command.DeviceId, now);

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return pair;
    }
}

public sealed class RevokeSessionHandler(
    IRefreshTokenRepository refreshTokens,
    ITokenIssuer tokens,
    IUnitOfWork unitOfWork,
    IClock clock) : ICommandHandler<RevokeSessionCommand, int>
{
    public async Task<int> HandleAsync(RevokeSessionCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var hash = tokens.HashRefreshToken(command.RefreshToken ?? string.Empty);
        var presented = await refreshTokens.GetByHashAsync(hash, cancellationToken).ConfigureAwait(false);

        if (presented is null)
        {
            // Se déconnecter avec un jeton inconnu n'est pas une erreur : le
            // résultat voulu est atteint.
            return 0;
        }

        var now = clock.UtcNow;
        var chain = await refreshTokens.ListBySessionAsync(presented.SessionId, cancellationToken).ConfigureAwait(false);

        var revoked = 0;

        foreach (var token in chain.Where(t => t.RevokedAt is null))
        {
            token.Revoke("Déconnexion.", now);
            revoked++;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return revoked;
    }
}

public sealed class RevokeAllSessionsHandler(
    IRefreshTokenRepository refreshTokens,
    ICallerContext caller,
    IUnitOfWork unitOfWork,
    IClock clock) : ICommandHandler<RevokeAllSessionsCommand, int>
{
    public async Task<int> HandleAsync(RevokeAllSessionsCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        // On ne révoque que ses propres sessions, ou celles d'autrui depuis le
        // back-office — jamais celles d'un autre utilisateur ordinaire.
        var isSelf = string.Equals(caller.SubjectId, command.AccountId.ToString(), StringComparison.Ordinal);
        var isBackOffice = caller.Roles.Overlaps(BackOfficeRoles);

        if (!isSelf && !isBackOffice)
        {
            throw new ForbiddenException("Vous ne pouvez révoquer que vos propres sessions.");
        }

        var now = clock.UtcNow;
        var tokens = await refreshTokens
            .ListActiveByAccountAsync(command.AccountId, cancellationToken)
            .ConfigureAwait(false);

        foreach (var token in tokens)
        {
            token.Revoke(isSelf ? "Déconnexion de tous les appareils." : "Révocation par le back-office.", now);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return tokens.Count;
    }

    private static readonly IReadOnlySet<string> BackOfficeRoles = Domain.Roles.BackOffice;
}
