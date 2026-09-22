using Hba.BuildingBlocks.Application.Abstractions;
using Hba.BuildingBlocks.Application.Messaging;
using Hba.BuildingBlocks.Domain;
using Hba.Identity.Application.Ports;
using Hba.Identity.Domain.Accounts;
using Hba.Identity.Domain.Otp;
using Hba.Identity.Domain.ValueObjects;

namespace Hba.Identity.Application.Authentication;

/// <summary>
/// Demande d'un code de connexion.
///
/// CETTE ROUTE EST PUBLIQUE ET NON AUTHENTIFIÉE. Elle doit donc répondre la
/// MÊME chose que le numéro soit inconnu, client, livreur, commerçant ou
/// suspendu — sinon elle devient un annuaire : n'importe qui pourrait tester
/// une liste de numéros pour savoir lesquels ont un compte chez HBA.
///
/// Concrètement : un défi est toujours créé et toujours renvoyé ; le SMS, lui,
/// ne part que si le numéro a effectivement le droit de se connecter ainsi.
/// Dans le cas contraire, aucun code n'existe et la vérification échouera avec
/// le message ordinaire « code incorrect ».
/// </summary>
public sealed class RequestOtpHandler(
    IAccountRepository accounts,
    IOtpStore store,
    IOtpRateLimiter rateLimiter,
    IOtpCodeService codes,
    IOtpNotifier notifier,
    IUnitOfWork unitOfWork,
    IClock clock) : ICommandHandler<RequestOtpCommand, RequestOtpResult>
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);

    public async Task<RequestOtpResult> HandleAsync(RequestOtpCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var phone = PhoneNumber.Create(command.Phone);

        // La limitation est par numéro. Elle protège le titulaire du numéro du
        // harcèlement par SMS et HBA du coût des envois ; elle ne dit rien de
        // l'existence d'un compte, puisqu'elle ne se déclenche que pour qui a
        // déjà demandé lui-même. La limitation par adresse, elle, est appliquée
        // au BFF, seul endroit où l'adresse du client est réelle.
        var retryAfter = await rateLimiter.TryAcquireAsync(phone.Value, cancellationToken).ConfigureAwait(false);

        if (retryAfter is not null)
        {
            throw new DomainException(
                "OTP_RATE_LIMITED",
                $"Un code a déjà été envoyé. Réessayez dans {(int)retryAfter.Value.TotalSeconds} secondes.");
        }

        var existing = await accounts.GetByPhoneAsync(phone.Value, cancellationToken).ConfigureAwait(false);
        var eligible = IsEligible(existing);

        var now = clock.UtcNow;
        var code = codes.GenerateCode();
        var challengeId = Guid.CreateVersion7();

        // L'empreinte dépend de l'identifiant du défi : le même code sur deux
        // demandes ne donne pas la même empreinte, et un code intercepté ne se
        // rejoue pas ailleurs.
        var challenge = OtpChallenge.Create(
            challengeId,
            phone.Value,
            codes.Hash(challengeId, code),
            command.Intent,
            command.DeviceId,
            eligible,
            now,
            Lifetime);

        await store.SaveAsync(challenge, cancellationToken).ConfigureAwait(false);

        if (eligible)
        {
            // Dépose la commande d'envoi dans l'Outbox ; c'est Notification qui
            // parlera à l'opérateur.
            notifier.SendOtp(phone.Value, code, Lifetime);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new RequestOtpResult(challenge.Id, challenge.ExpiresAt, (int)Lifetime.TotalSeconds);
    }

    /// <summary>
    /// Un numéro sans compte est éligible : c'est une inscription. Un compte
    /// existant l'est s'il est actif et se connecte bien par SMS — un compte de
    /// commerçant ou de back-office a un mot de passe et le portail web.
    /// </summary>
    private static bool IsEligible(Account? account)
    {
        if (account is null)
        {
            return true;
        }

        if (account.Status != AccountStatus.Active)
        {
            return false;
        }

        return account.Roles.Any(Domain.Roles.SelfServiceByOtp.Contains);
    }
}
