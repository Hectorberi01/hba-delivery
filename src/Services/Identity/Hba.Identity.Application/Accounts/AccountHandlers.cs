using Hba.BuildingBlocks.Application.Abstractions;
using Hba.BuildingBlocks.Application.Messaging;
using Hba.BuildingBlocks.Domain;
using Hba.BuildingBlocks.Security;
using Hba.Identity.Application.Ports;
using Hba.Identity.Application.Views;
using Hba.Identity.Domain.Accounts;
using Hba.Identity.Domain.ValueObjects;

namespace Hba.Identity.Application.Accounts;

/// <summary>
/// Vérifications d'accès communes aux opérations d'administration des comptes.
/// Elles sont faites ici, dans le service, et pas au BFF.
/// </summary>
internal static class AccountGuard
{
    public static void EnsureAdmin(ICallerContext caller)
    {
        ArgumentNullException.ThrowIfNull(caller);

        if (!caller.IsInRole(HbaRoles.Admin))
        {
            throw new ForbiddenException("Réservé au rôle admin.");
        }
    }

    public static void EnsureBackOffice(ICallerContext caller)
    {
        ArgumentNullException.ThrowIfNull(caller);

        if (!caller.Roles.Overlaps(HbaRoles.BackOffice))
        {
            throw new ForbiddenException("Réservé au back-office.");
        }
    }
}

public sealed class CreateBackOfficeAccountHandler(
    IAccountRepository accounts,
    ICallerContext caller,
    IUnitOfWork unitOfWork,
    IClock clock) : ICommandHandler<CreateBackOfficeAccountCommand, AccountView>
{
    public async Task<AccountView> HandleAsync(
        CreateBackOfficeAccountCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        AccountGuard.EnsureAdmin(caller);

        var email = EmailAddress.Create(command.Email);

        if (await accounts.GetByEmailAsync(email.Value, cancellationToken).ConfigureAwait(false) is not null)
        {
            throw new DomainException("EMAIL_ALREADY_USED", "Un compte existe déjà avec cette adresse.");
        }

        var account = Account.CreateBackOffice(
            Guid.CreateVersion7(),
            email,
            command.DisplayName,
            command.Roles,
            PasswordHash.FromPlainText(command.InitialPassword),
            caller.ToActor(),
            clock.UtcNow);

        accounts.Add(account);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return AccountViewMapper.ToView(account);
    }
}

public sealed class CreateMerchantAccountHandler(
    IAccountRepository accounts,
    ICallerContext caller,
    IUnitOfWork unitOfWork,
    IClock clock) : ICommandHandler<CreateMerchantAccountCommand, AccountView>
{
    public async Task<AccountView> HandleAsync(
        CreateMerchantAccountCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        // Un propriétaire peut créer ses propres employés ; tout le reste est du
        // ressort du back-office.
        var isOwnerAddingStaff = caller.IsInRole(HbaRoles.MerchantOwner)
                                 && string.Equals(caller.MerchantId, command.MerchantId, StringComparison.Ordinal)
                                 && string.Equals(command.Role, HbaRoles.MerchantStaff, StringComparison.Ordinal);

        if (!isOwnerAddingStaff)
        {
            AccountGuard.EnsureAdmin(caller);
        }

        var email = string.IsNullOrWhiteSpace(command.Email) ? null : EmailAddress.Create(command.Email);
        var phone = string.IsNullOrWhiteSpace(command.Phone) ? null : PhoneNumber.Create(command.Phone);

        if (email is not null
            && await accounts.GetByEmailAsync(email.Value, cancellationToken).ConfigureAwait(false) is not null)
        {
            throw new DomainException("EMAIL_ALREADY_USED", "Un compte existe déjà avec cette adresse.");
        }

        if (phone is not null
            && await accounts.PhoneExistsAsync(phone.Value, cancellationToken).ConfigureAwait(false))
        {
            throw new DomainException("PHONE_ALREADY_USED", "Un compte existe déjà avec ce numéro.");
        }

        var account = Account.CreateMerchantUser(
            Guid.CreateVersion7(),
            command.MerchantId,
            email,
            phone,
            command.DisplayName,
            command.Role,
            PasswordHash.FromPlainText(command.InitialPassword),
            caller.ToActor(),
            clock.UtcNow);

        accounts.Add(account);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return AccountViewMapper.ToView(account);
    }
}

public sealed class SetAccountRolesHandler(
    IAccountRepository accounts,
    ICallerContext caller,
    IUnitOfWork unitOfWork,
    IClock clock) : ICommandHandler<SetAccountRolesCommand, AccountView>
{
    public async Task<AccountView> HandleAsync(SetAccountRolesCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        AccountGuard.EnsureAdmin(caller);

        if (string.Equals(caller.SubjectId, command.AccountId.ToString(), StringComparison.Ordinal))
        {
            // Sinon le dernier administrateur peut se retirer le rôle admin et
            // plus personne ne peut le lui rendre.
            throw new ForbiddenException("Un administrateur ne modifie pas ses propres rôles.");
        }

        var account = await accounts.GetByIdAsync(command.AccountId, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException("Compte", command.AccountId.ToString());

        account.SetRoles(command.Roles, command.Reason, caller.ToActor(), clock.UtcNow);

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return AccountViewMapper.ToView(account);
    }
}

public sealed class SuspendAccountHandler(
    IAccountRepository accounts,
    IRefreshTokenRepository refreshTokens,
    ICallerContext caller,
    IUnitOfWork unitOfWork,
    IClock clock) : ICommandHandler<SuspendAccountCommand, AccountView>
{
    public async Task<AccountView> HandleAsync(SuspendAccountCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        AccountGuard.EnsureBackOffice(caller);

        var now = clock.UtcNow;

        var account = await accounts.GetByIdAsync(command.AccountId, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException("Compte", command.AccountId.ToString());

        account.Suspend(command.Reason, caller.ToActor(), now);

        // Suspendre sans révoquer laisserait la personne travailler jusqu'à
        // l'expiration de son jeton d'accès.
        var sessions = await refreshTokens
            .ListActiveByAccountAsync(account.Id, cancellationToken)
            .ConfigureAwait(false);

        foreach (var token in sessions)
        {
            token.Revoke("Compte suspendu.", now);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return AccountViewMapper.ToView(account);
    }
}

public sealed class ReactivateAccountHandler(
    IAccountRepository accounts,
    ICallerContext caller,
    IUnitOfWork unitOfWork,
    IClock clock) : ICommandHandler<ReactivateAccountCommand, AccountView>
{
    public async Task<AccountView> HandleAsync(ReactivateAccountCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        AccountGuard.EnsureBackOffice(caller);

        var account = await accounts.GetByIdAsync(command.AccountId, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException("Compte", command.AccountId.ToString());

        account.Reactivate(command.Reason, caller.ToActor(), clock.UtcNow);

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return AccountViewMapper.ToView(account);
    }
}

public sealed class SetPasswordHandler(
    IAccountRepository accounts,
    IRefreshTokenRepository refreshTokens,
    ICallerContext caller,
    IUnitOfWork unitOfWork,
    IClock clock) : ICommandHandler<SetPasswordCommand, Unit>
{
    public async Task<Unit> HandleAsync(SetPasswordCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var now = clock.UtcNow;
        var isSelf = string.Equals(caller.SubjectId, command.AccountId.ToString(), StringComparison.Ordinal);

        if (!isSelf)
        {
            AccountGuard.EnsureAdmin(caller);
        }

        var account = await accounts.GetByIdAsync(command.AccountId, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException("Compte", command.AccountId.ToString());

        if (isSelf && account.HasPassword && !account.TryPassword(command.CurrentPassword, now))
        {
            await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            throw new ForbiddenException("Mot de passe actuel incorrect.");
        }

        account.SetPassword(PasswordHash.FromPlainText(command.NewPassword), caller.ToActor(), now);

        // Changer de mot de passe ferme les autres sessions : c'est le geste
        // qu'on fait quand on pense avoir été compromis.
        var sessions = await refreshTokens
            .ListActiveByAccountAsync(account.Id, cancellationToken)
            .ConfigureAwait(false);

        foreach (var token in sessions)
        {
            token.Revoke("Mot de passe modifié.", now);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Unit.Value;
    }
}

public sealed class LinkDriverProfileHandler(
    IAccountRepository accounts,
    ICallerContext caller,
    IUnitOfWork unitOfWork,
    IClock clock) : ICommandHandler<LinkDriverProfileCommand, AccountView>
{
    public async Task<AccountView> HandleAsync(LinkDriverProfileCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        AccountGuard.EnsureBackOffice(caller);

        var account = await accounts.GetByIdAsync(command.AccountId, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException("Compte", command.AccountId.ToString());

        account.LinkDriverProfile(command.DriverId, caller.ToActor(), clock.UtcNow);

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return AccountViewMapper.ToView(account);
    }
}
