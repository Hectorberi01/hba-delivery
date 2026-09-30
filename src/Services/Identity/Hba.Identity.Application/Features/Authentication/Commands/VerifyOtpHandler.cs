using Hba.BuildingBlocks.Application.Abstractions;
using Hba.BuildingBlocks.Application.Messaging;
using Hba.BuildingBlocks.Domain;
using Hba.Identity.Application.Common.Interfaces;
using Hba.Identity.Application.Common.Services;
using Hba.Identity.Application.Common.Views;
using Hba.Identity.Domain.Accounts;
using Hba.Identity.Domain.Exceptions;
using Hba.Identity.Domain.Interfaces;
using Hba.Identity.Domain.Otp;
using Hba.Identity.Domain.ValueObjects;

namespace Hba.Identity.Application.Features.Authentication.Commands;

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
            ?? throw new DomainException(IdentityErrorCodes.OtpNotFound, "Ce code n'est plus valable. Demandez-en un nouveau.");

        var candidate = codes.Hash(challenge.Id, command.Code ?? string.Empty);

        // Aucun code n'a été envoyé pour ce défi : le numéro appartient à un
        // compte qui ne se connecte pas par SMS, ou à un compte suspendu. Le
        // message est le même que pour un code faux, sans quoi la route
        // redeviendrait un moyen de sonder les numéros.
        if (!challenge.Eligible)
        {
            challenge.Verify(candidate, now);
            await store.SaveAsync(challenge, cancellationToken).ConfigureAwait(false);
            throw new DomainException(IdentityErrorCodes.InvalidOtp, "Code incorrect.");
        }

        if (!challenge.Verify(candidate, now))
        {
            // Le compteur de tentatives vient d'être incrémenté : il faut le
            // conserver, sinon les essais seraient illimités.
            await store.SaveAsync(challenge, cancellationToken).ConfigureAwait(false);
            throw new DomainException(IdentityErrorCodes.InvalidOtp, "Code incorrect.");
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
                throw new DomainException(IdentityErrorCodes.InvalidOtp, "Code incorrect.");
            }
        }

        // SE RECONNECTER ANNULE LA DEMANDE DE SUPPRESSION.
        //
        // C'est ce que les deux écrans promettent — celui de l'application et
        // celui du profil — et ce n'était implémenté nulle part : il fallait
        // retrouver le profil et appuyer sur « Garder mon compte », ce que rien
        // ne disait. Un titulaire qui revenait dans les trente jours voyait donc
        // son compte effacé quand même.
        //
        // APRÈS LA VÉRIFICATION DU CODE, ET NON AVANT : c'est la preuve que
        // c'est bien le titulaire du numéro qui revient. La méthode du domaine
        // ne fait rien si le compte n'est pas en sursis.
        account.CancelDeletion(ActeurTitulaire(account), now);

        account.RecordLogin(now);

        var pair = sessions.Issue(account, command.DeviceId ?? challenge.DeviceId, Guid.CreateVersion7(), now);

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return pair;
    }

    /// <summary>
    /// Le titulaire, comme acteur d'audit.
    /// </summary>
    ///
    /// <remarks>
    /// IL N'Y A PAS DE CONTEXTE D'APPEL ICI, ET C'EST NORMAL : on est en train
    /// de l'authentifier. L'acteur se déduit donc du compte lui-même. Seuls
    /// « customer » et « driver » passent par ce chemin — SelfServiceByOtp —
    /// donc deux cas suffisent, et l'identifiant retenu est celui du compte,
    /// comme le fait déjà CallerContext quand le jeton ne porte pas de
    /// driver_id.
    /// </remarks>
    private static Actor ActeurTitulaire(Account account)
        => account.Roles.Contains(Domain.Roles.Driver)
            ? Actor.Driver(account.Id.ToString())
            : Actor.Customer(account.Id.ToString());
}
