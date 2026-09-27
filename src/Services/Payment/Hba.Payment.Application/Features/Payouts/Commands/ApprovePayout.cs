using Hba.BuildingBlocks.Application.Abstractions;
using Hba.BuildingBlocks.Application.Messaging;
using Hba.BuildingBlocks.Domain;
using Hba.Payment.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;

namespace Hba.Payment.Application.Features.Payouts.Commands;

/// <summary>
/// La finance est d'accord pour payer.
///
/// APPROUVER N'EST PAS PAYER, et le systeme s'interdit de le confondre. Ce
/// geste dit « le dossier est bon » : il n'ecrit RIEN au grand livre. Le
/// virement se fait ensuite, a la main, chez l'operateur — il peut echouer,
/// attendre un solde, partir sur un mauvais numero. Debiter ici ferait
/// disparaitre du compte du livreur un argent qu'il n'a pas recu, et le jour
/// ou il viendrait le reclamer, le releve lui donnerait tort.
///
/// CE QUE L'APPROBATION CHANGE QUAND MEME : la demande reste en cours, donc le
/// livreur ne peut pas en ouvrir une seconde, et elle reste dans la file de la
/// finance jusqu'a ce que la reference du virement y soit consignee. Une
/// demande approuvee et oubliee est visible ; c'est fait pour.
/// </summary>
public sealed record ApprovePayoutCommand(Guid PayoutId) : ICommand<PayoutRequestView>;

public sealed class ApprovePayoutHandler(
    IPayoutRequestRepository payouts,
    IUnitOfWork unitOfWork,
    ICallerContext caller,
    IClock clock,
    ILogger<ApprovePayoutHandler> logger) : ICommandHandler<ApprovePayoutCommand, PayoutRequestView>
{
    public async Task<PayoutRequestView> HandleAsync(
        ApprovePayoutCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        PayoutReview.EnsureReviewer(caller);

        var demande = await payouts.GetByIdAsync(command.PayoutId, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException("Demande de versement", command.PayoutId.ToString());

        var signature = PayoutReview.Signature(caller);

        demande.Approve(signature, clock.UtcNow);

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // L'AUDIT EST DANS LE JOURNAL ET DANS L'AGREGAT, LES DEUX. L'agregat
        // garde qui a decide et quand, parce que le livreur et la finance
        // doivent pouvoir le lire ; le journal garde en plus le TraceId, donc
        // le chemin complet de la requete, ce qu'aucune colonne ne porte.
        logger.LogInformation(
            "Versement approuve : demande {PayoutId} de {AmountXof} F au livreur {DriverId}, par {Acteur}. " +
            "Aucune ecriture au grand livre a ce stade.",
            demande.Id,
            demande.AmountXof,
            demande.DriverId,
            caller.ToActor().ToString());

        return PayoutReview.ToView(demande);
    }
}
