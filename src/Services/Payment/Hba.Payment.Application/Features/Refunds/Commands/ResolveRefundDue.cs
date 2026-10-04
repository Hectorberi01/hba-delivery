using Hba.BuildingBlocks.Application.Abstractions;
using Hba.BuildingBlocks.Application.Messaging;
using Hba.BuildingBlocks.Domain;
using Hba.BuildingBlocks.Security;
using Hba.Payment.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;

namespace Hba.Payment.Application.Features.Refunds.Commands;

/// <summary>
/// La finance a rendu l'argent, et le note ici.
/// </summary>
///
/// <remarks>
/// AUCUN ARGENT NE PART D'ICI, et c'est le point qu'il faut avoir en tete en lisant
/// ce fichier. FedaPay n'expose pas de remboursement par API — tableau de bord
/// marchand uniquement, MTN Mobile Money seulement, verifie le 30 septembre 2026.
/// Cette commande enregistre un geste DEJA ACCOMPLI ailleurs. La confondre avec un
/// ordre de paiement ferait croire que le systeme rembourse, alors qu'il tient un
/// cahier.
/// </remarks>
public sealed record SettleRefundDueCommand(
    Guid RefundDueId,
    long SettledAmount,
    string? Note) : ICommand<RefundDueView>;

/// <summary>Rien n'est du, apres examen. Le motif est obligatoire.</summary>
public sealed record DismissRefundDueCommand(
    Guid RefundDueId,
    string Note) : ICommand<RefundDueView>;

public sealed class SettleRefundDueHandler(
    IRefundDueRepository refunds,
    IUnitOfWork unitOfWork,
    ICallerContext caller,
    IClock clock,
    ILogger<SettleRefundDueHandler> logger) : ICommandHandler<SettleRefundDueCommand, RefundDueView>
{
    public async Task<RefundDueView> HandleAsync(
        SettleRefundDueCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        RefundAccess.EnsureCanResolve(caller);

        var ligne = await refunds.GetByIdAsync(command.RefundDueId, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException("Remboursement a faire", command.RefundDueId.ToString());

        ligne.Settle(command.SettledAmount, caller.SubjectId, command.Note, clock.UtcNow);

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // L'ARGENT QUI SORT SE JOURNALISE, TOUJOURS, ET AVEC SON AUTEUR. C'est la
        // seule trace qui reste si le tableau de bord du fournisseur et notre base
        // divergent un jour.
        logger.LogInformation(
            "Remboursement {Id} marque rendu par {Acteur} : {Rendu} XOF sur {Encaisse} encaisses.",
            ligne.Id,
            caller.SubjectId,
            command.SettledAmount,
            ligne.AmountCollected);

        return RefundDueView.From(ligne);
    }
}

public sealed class DismissRefundDueHandler(
    IRefundDueRepository refunds,
    IUnitOfWork unitOfWork,
    ICallerContext caller,
    IClock clock,
    ILogger<DismissRefundDueHandler> logger) : ICommandHandler<DismissRefundDueCommand, RefundDueView>
{
    public async Task<RefundDueView> HandleAsync(
        DismissRefundDueCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        RefundAccess.EnsureCanResolve(caller);

        var ligne = await refunds.GetByIdAsync(command.RefundDueId, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException("Remboursement a faire", command.RefundDueId.ToString());

        ligne.Dismiss(caller.SubjectId, command.Note, clock.UtcNow);

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // EN Warning : garder l'argent d'un client est une decision, pas une
        // operation de routine. Elle doit se retrouver dans un journal sans qu'on ait
        // a la chercher.
        logger.LogWarning(
            "Remboursement {Id} classe sans suite par {Acteur} : {Montant} XOF conserves. Motif : {Motif}",
            ligne.Id,
            caller.SubjectId,
            ligne.AmountCollected,
            command.Note);

        return RefundDueView.From(ligne);
    }
}
