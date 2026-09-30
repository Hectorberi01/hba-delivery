using Hba.BuildingBlocks.Application.Abstractions;
using Hba.BuildingBlocks.Application.Messaging;
using Hba.BuildingBlocks.Domain;
using Hba.Identity.Application.Common.Interfaces;
using Hba.Identity.Domain.Accounts;
using Hba.Identity.Domain.Exceptions;
using Hba.Identity.Domain.Interfaces;
using Hba.Identity.Domain.Otp;
using Hba.Identity.Domain.ValueObjects;

namespace Hba.Identity.Application.Features.Authentication.Commands;

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
            throw new DomainException(IdentityErrorCodes.OtpRateLimited, $"Un code a déjà été envoyé. Réessayez dans {(int)retryAfter.Value.TotalSeconds} secondes.");
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
            //
            // PREMIERE CONNEXION : PAS DE COMPTE, DONC PAS DE CONSENTEMENT, DONC
            // SMS. C'est inévitable et ce n'est pas un défaut : le consentement
            // se recueille à l'inscription, qui n'a pas encore eu lieu quand ce
            // premier code part. WhatsApp ne prend le relais qu'à partir de la
            // connexion suivante.
            notifier.SendOtp(phone.Value, code, Lifetime, existing?.CanReceiveWhatsApp ?? false);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new RequestOtpResult(challenge.Id, challenge.ExpiresAt, (int)Lifetime.TotalSeconds);
    }

    /// <summary>
    /// Un numéro sans compte est éligible : c'est une inscription. Un compte
    /// existant l'est s'il se connecte bien par SMS — un compte de commerçant ou
    /// de back-office a un mot de passe et le portail web.
    /// </summary>
    ///
    /// <remarks>
    /// UN COMPTE EN SURSIS DE SUPPRESSION EST ÉLIGIBLE, ET C'ÉTAIT LE DÉFAUT.
    ///
    /// La condition exigeait « Active ». Un titulaire qui avait demandé la
    /// suppression de son compte ne recevait donc plus AUCUN SMS : le défi était
    /// marqué non éligible, et la vérification répondait « Code incorrect. » à
    /// chaque tentative. Son numéro étant unique, il ne pouvait pas non plus
    /// recréer de compte. Il restait dehors trente jours, puis tout était
    /// effacé — alors que les deux écrans lui promettent l'inverse : « vous
    /// reconnecter avant cette date vous permettra de tout garder ».
    ///
    /// LE REFUS ÉTAIT AU MAUVAIS ÉTAGE. Account.EnsureCanAuthenticate dit
    /// exactement ce qu'il faut et ne bloque que « Suspended », avec la raison
    /// écrite à côté de l'énumération : se reconnecter EST le geste qui annule
    /// la demande. C'est cette éligibilité-ci, un cran plus haut, que personne
    /// n'avait mise à jour.
    ///
    /// ON N'ÉNUMÈRE TOUJOURS RIEN : un compte suspendu reste non éligible, et le
    /// message rendu est le même dans tous les cas.
    /// </remarks>
    private static bool IsEligible(Account? account)
    {
        if (account is null) { return true; }
        if (account.Status is not (AccountStatus.Active or AccountStatus.PendingDeletion)) { return false; }
        return account.Roles.Any(Domain.Roles.SelfServiceByOtp.Contains);
    }
}
