using Hba.BuildingBlocks.Application.Abstractions;
using Hba.BuildingBlocks.Application.Messaging;
using Hba.BuildingBlocks.Domain;
using Hba.Identity.Application.Ports;
using Hba.Identity.Application.Services;
using Hba.Identity.Application.Views;
using Hba.Identity.Domain.Accounts;
using Hba.Identity.Domain.Otp;
using Hba.Identity.Domain.ValueObjects;

namespace Hba.Identity.Application.Authentication;

public sealed class VerifyOtpHandler(
    IAccountRepository accounts,
    IOtpStore store,
    IOtpCodeService codes,
    SessionIssuer sessions,
    IUnitOfWork unitOfWork,
    IClock clock) : ICommandHandler<VerifyOtpCommand, TokenPairView>
{
    public async Task<TokenPairView> HandleAsync(VerifyOtpCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var now = clock.UtcNow;

        var challenge = await store.GetAsync(command.ChallengeId, cancellationToken).ConfigureAwait(false)
            ?? throw new DomainException("OTP_NOT_FOUND", "Ce code n'est plus valable. Demandez-en un nouveau.");

        var candidate = codes.Hash(challenge.Id, command.Code ?? string.Empty);

        // Aucun code n'a été envoyé pour ce défi : le numéro appartient à un
        // compte qui ne se connecte pas par SMS, ou à un compte suspendu. Le
        // message est le même que pour un code faux, sans quoi la route
        // redeviendrait un moyen de sonder les numéros.
        if (!challenge.Eligible)
        {
            challenge.Verify(candidate, now);
            await store.SaveAsync(challenge, cancellationToken).ConfigureAwait(false);
            throw new DomainException("INVALID_OTP", "Code incorrect.");
        }

        if (!challenge.Verify(candidate, now))
        {
            // Le compteur de tentatives vient d'être incrémenté : il faut le
            // conserver, sinon les essais seraient illimités.
            await store.SaveAsync(challenge, cancellationToken).ConfigureAwait(false);
            throw new DomainException("INVALID_OTP", "Code incorrect.");
        }

        // Un code valide ne sert qu'une fois.
        await store.DeleteAsync(challenge.Id, cancellationToken).ConfigureAwait(false);

        var account = await accounts.GetByPhoneAsync(challenge.Phone, cancellationToken).ConfigureAwait(false);

        if (account is null)
        {
            var role = challenge.Intent == OtpIntent.Driver ? Domain.Roles.Driver : Domain.Roles.Customer;

            account = Account.RegisterWithPhone(
                Guid.CreateVersion7(),
                PhoneNumber.Create(challenge.Phone),
                command.DisplayName ?? string.Empty,
                role,
                now);

            accounts.Add(account);
        }
        else
        {
            account.EnsureCanAuthenticate();

            // Ceinture et bretelles : le défi n'était pas éligible, donc aucun
            // code n'a pu être saisi correctement. Si cela arrivait quand même,
            // rien ne doit permettre de se connecter par SMS à un compte qui a
            // un mot de passe.
            if (!account.Roles.Any(Domain.Roles.SelfServiceByOtp.Contains))
            {
                throw new DomainException("INVALID_OTP", "Code incorrect.");
            }
        }

        account.RecordLogin(now);

        var pair = sessions.Issue(account, command.DeviceId ?? challenge.DeviceId, Guid.CreateVersion7(), now);

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return pair;
    }
}
