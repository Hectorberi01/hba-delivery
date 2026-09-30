using Hba.BuildingBlocks.Application.Abstractions;
using Hba.BuildingBlocks.Application.Messaging;
using Hba.BuildingBlocks.Domain;
using Hba.Identity.Domain.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Hba.Identity.Application.Features.Accounts.Commands;

/// <summary>
/// Le délai de grâce entre la demande et l'effacement.
/// </summary>
///
/// <remarks>
/// TRENTE JOURS, ET C'EST UN REGLAGE, PAS UNE CONSTANTE. La durée est un choix
/// de produit — elle peut devoir suivre une exigence de magasin ou une
/// recommandation d'autorité — et une constante obligerait à recompiler pour en
/// changer.
///
/// CHANGER LE REGLAGE NE DEPLACE AUCUNE ECHEANCE DEJA ANNONCEE : la date est
/// figée dans le compte au moment de la demande, pas recalculée à la lecture.
/// Ce qui a été promis à un client est ce qui s'applique à lui.
/// </remarks>
public sealed class AccountDeletionOptions
{
    public const string Section = "AccountDeletion";

    public int GraceDays { get; set; } = 30;

    /// <summary>Intervalle entre deux passages du travail d'effacement.</summary>
    public int SweepMinutes { get; set; } = 60;

    /// <summary>Nombre maximal de comptes effacés par passage.</summary>
    ///
    /// <remarks>
    /// UN PLAFOND, PARCE QU'UN EFFACEMENT DECLENCHE DES EVENEMENTS. Chaque
    /// compte effacé fait travailler Directory et Media ; mille d'un coup après
    /// un arrêt prolongé du service saturerait le topic avant que quiconque ne
    /// s'en aperçoive. Le passage suivant reprend la suite.
    /// </remarks>
    public int BatchSize { get; set; } = 100;
}

/// <summary>Ce que le titulaire a le droit de savoir de sa demande.</summary>
public sealed record AccountDeletionView(
    bool Requested,
    DateTimeOffset? RequestedAt,
    DateTimeOffset? ScheduledFor);

public sealed record RequestAccountDeletionCommand : ICommand<AccountDeletionView>;

public sealed record CancelAccountDeletionCommand : ICommand<AccountDeletionView>;

public sealed record GetAccountDeletionQuery : IQuery<AccountDeletionView>;

public sealed class RequestAccountDeletionHandler(
    IAccountRepository accounts,
    IRefreshTokenRepository refreshTokens,
    ICallerContext caller,
    IUnitOfWork unitOfWork,
    IClock clock,
    IOptions<AccountDeletionOptions> options,
    ILogger<RequestAccountDeletionHandler> journal)
    : ICommandHandler<RequestAccountDeletionCommand, AccountDeletionView>
{
    public async Task<AccountDeletionView> HandleAsync(
        RequestAccountDeletionCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(options);

        var id = ComptePropre.Resoudre(caller);

        var account = await accounts.GetByIdAsync(id, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException("Compte", id.ToString());

        var now = clock.UtcNow;
        var echeance = now.AddDays(options.Value.GraceDays);

        account.RequestDeletion(echeance, caller.ToActor(), now);

        // LES SESSIONS TOMBENT TOUT DE SUITE, ET C'EST CE QUI REND LE DELAI
        // UTILE. Sans cela, le téléphone resterait connecté et le client
        // « annulerait » sa demande sans le savoir dès le prochain écran. En le
        // déconnectant, on fait de la reconnexion un GESTE : c'est là qu'on lui
        // demandera s'il veut vraiment revenir.
        var sessions = await refreshTokens
            .ListActiveByAccountAsync(account.Id, cancellationToken)
            .ConfigureAwait(false);

        foreach (var token in sessions)
        {
            token.Revoke("Suppression du compte demandée.", now);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        journal.LogInformation(
            "Suppression demandee pour le compte {AccountId}, effacement prevu le {Echeance:yyyy-MM-dd}.",
            account.Id,
            echeance);

        return Vue(account);
    }

    internal static AccountDeletionView Vue(Domain.Accounts.Account account)
        => new(
            account.IsPendingDeletion,
            account.DeletionRequestedAt,
            account.DeletionScheduledFor);
}

public sealed class CancelAccountDeletionHandler(
    IAccountRepository accounts,
    ICallerContext caller,
    IUnitOfWork unitOfWork,
    IClock clock,
    ILogger<CancelAccountDeletionHandler> journal)
    : ICommandHandler<CancelAccountDeletionCommand, AccountDeletionView>
{
    public async Task<AccountDeletionView> HandleAsync(
        CancelAccountDeletionCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var id = ComptePropre.Resoudre(caller);

        var account = await accounts.GetByIdAsync(id, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException("Compte", id.ToString());

        account.CancelDeletion(caller.ToActor(), clock.UtcNow);

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        journal.LogInformation("Suppression annulee pour le compte {AccountId}.", account.Id);

        return RequestAccountDeletionHandler.Vue(account);
    }
}

public sealed class GetAccountDeletionHandler(
    IAccountRepository accounts,
    ICallerContext caller) : IQueryHandler<GetAccountDeletionQuery, AccountDeletionView>
{
    public async Task<AccountDeletionView> HandleAsync(
        GetAccountDeletionQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var id = ComptePropre.Resoudre(caller);

        var account = await accounts.GetByIdAsync(id, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException("Compte", id.ToString());

        return RequestAccountDeletionHandler.Vue(account);
    }
}

/// <summary>
/// Efface les comptes dont l'échéance est atteinte.
/// </summary>
///
/// <remarks>
/// UNE COMMANDE, ET NON DU CODE DANS LE SERVICE HEBERGE. Le travail planifié se
/// contente de la déclencher : la règle — qui est dû, ce qu'on lève, ce qu'on
/// supprime — reste dans la couche applicative, là où elle se teste sans
/// démarrer d'hôte.
/// </remarks>
public sealed record EraseDueAccountsCommand : ICommand<int>;

public sealed class EraseDueAccountsHandler(
    IAccountRepository accounts,
    IUnitOfWork unitOfWork,
    IClock clock,
    IOptions<AccountDeletionOptions> options,
    ILogger<EraseDueAccountsHandler> journal) : ICommandHandler<EraseDueAccountsCommand, int>
{
    public async Task<int> HandleAsync(EraseDueAccountsCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(options);

        var now = clock.UtcNow;

        var dus = await accounts
            .ListDeletionsDueAsync(now, options.Value.BatchSize, cancellationToken)
            .ConfigureAwait(false);

        if (dus.Count == 0)
        {
            return 0;
        }

        foreach (var account in dus)
        {
            // L'ORDRE EST LE POINT DELICAT DE TOUT CE FICHIER.
            //
            // MarkErased LEVE l'événement ; Remove supprime la ligne ;
            // SaveChanges écrit les deux DANS LA MEME TRANSACTION, l'événement
            // partant par l'Outbox. Inverser reviendrait à supprimer un agrégat
            // dont plus personne ne peut lever l'événement — le compte
            // disparaîtrait d'Identity, et Directory garderait le nom, le
            // courriel et les adresses pour toujours, sans que rien ne le
            // signale.
            //
            // LE ChangeTracker VOIT ENCORE L'AGREGAT SUPPRIME : c'est ce qui
            // permet au DbContext de drainer ses événements au SaveChanges.
            account.MarkErased(Actor.Scheduler, now);
            accounts.Remove(account);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // « WARNING » ET NON « INFORMATION », POUR UNE OPERATION NORMALE. Un
        // effacement est irréversible et concerne une personne : le jour où
        // quelqu'un demandera ce qu'est devenu son compte, cette ligne est la
        // seule chose qui restera pour répondre.
        journal.LogWarning("{Nombre} compte(s) efface(s) definitivement.", dus.Count);

        return dus.Count;
    }
}
