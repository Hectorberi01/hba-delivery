using Hba.BuildingBlocks.Application.Abstractions;
using Hba.BuildingBlocks.Application.Messaging;
using Hba.BuildingBlocks.Domain;
using Hba.Identity.Domain.Interfaces;

namespace Hba.Identity.Application.Features.Accounts.Commands;

/// <summary>
/// Consentement à recevoir des messages WhatsApp, lu et modifié par le
/// titulaire du compte.
/// </summary>
///
/// <remarks>
/// CE QUI MANQUAIT N'ETAIT PAS LE DOMAINE. Account.SetWhatsAppConsent existe
/// depuis le 25 septembre, avec sa colonne, son événement auditable et
/// CanReceiveWhatsApp qui décide déjà du canal d'envoi du code de connexion.
/// Il n'y avait simplement aucune commande pour l'appeler, donc aucune route,
/// donc un écran qui annonçait « à venir » devant un mécanisme complet.
///
/// LE REFERENTIEL EXIGE EXPLICITE ET REVOCABLE. Explicite : le défaut est faux,
/// et rien ne l'accorde sans un geste. Révocable : la même commande retire le
/// consentement, par le même chemin — un retrait plus difficile qu'un accord ne
/// serait pas une révocation.
/// </remarks>
public sealed record WhatsAppConsentView(bool Granted, DateTimeOffset? GrantedAt);

public sealed record GetWhatsAppConsentQuery : IQuery<WhatsAppConsentView>;

public sealed record SetWhatsAppConsentCommand(bool Granted) : ICommand<WhatsAppConsentView>;

/// <summary>Le compte visé est toujours celui du jeton, jamais un autre.</summary>
internal static class ComptePropre
{
    public static Guid Resoudre(ICallerContext caller)
    {
        ArgumentNullException.ThrowIfNull(caller);

        if (!caller.IsAuthenticated)
        {
            throw new ForbiddenException("Appel non authentifié.");
        }

        return Guid.TryParse(caller.SubjectId, out var id)
            ? id
            : throw new NotFoundException("Compte", caller.SubjectId);
    }
}

public sealed class GetWhatsAppConsentHandler(
    IAccountRepository accounts,
    ICallerContext caller) : IQueryHandler<GetWhatsAppConsentQuery, WhatsAppConsentView>
{
    public async Task<WhatsAppConsentView> HandleAsync(
        GetWhatsAppConsentQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var id = ComptePropre.Resoudre(caller);

        var account = await accounts.GetByIdAsync(id, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException("Compte", id.ToString());

        return new WhatsAppConsentView(account.WhatsAppOptIn, account.WhatsAppOptInAt);
    }
}

public sealed class SetWhatsAppConsentHandler(
    IAccountRepository accounts,
    ICallerContext caller,
    IUnitOfWork unitOfWork,
    IClock clock) : ICommandHandler<SetWhatsAppConsentCommand, WhatsAppConsentView>
{
    public async Task<WhatsAppConsentView> HandleAsync(
        SetWhatsAppConsentCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var id = ComptePropre.Resoudre(caller);

        var account = await accounts.GetByIdAsync(id, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException("Compte", id.ToString());

        // IDEMPOTENT COTE DOMAINE : redonner un consentement déjà donné ne lève
        // pas d'événement, et n'écrase donc pas la date du premier accord.
        account.SetWhatsAppConsent(command.Granted, caller.ToActor(), clock.UtcNow);

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new WhatsAppConsentView(account.WhatsAppOptIn, account.WhatsAppOptInAt);
    }
}
