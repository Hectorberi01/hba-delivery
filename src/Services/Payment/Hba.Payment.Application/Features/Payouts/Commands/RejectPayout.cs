using Hba.BuildingBlocks.Application.Abstractions;
using Hba.BuildingBlocks.Application.Messaging;
using Hba.BuildingBlocks.Domain;
using Hba.Payment.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;

namespace Hba.Payment.Application.Features.Payouts.Commands;

/// <summary>
/// La finance refuse, avec un motif.
///
/// LE MOTIF EST OBLIGATOIRE, ET LE DOMAINE LE FAIT RESPECTER. Un refus muet
/// laisse le livreur sans rien a corriger : il redemandera le lendemain la
/// meme somme, la finance refusera de nouveau, et personne n'aura avance. Le
/// motif est la seule chose qui transforme un refus en instruction.
///
/// IL EST ECRIT POUR ETRE LU PAR LE LIVREUR. Ce n'est pas une note interne :
/// il remonte a l'ecran des gains. L'ecran de la console le dit, pour qu'on
/// n'y ecrive pas « RAS, cf. tel ».
///
/// UN REFUS LIBERE LA PLACE : la demande n'est plus en cours, le livreur peut
/// en ouvrir une autre — corrigee, s'il a compris le motif.
/// </summary>
public sealed record RejectPayoutCommand(Guid PayoutId, string Reason) : ICommand<PayoutRequestView>;

public sealed class RejectPayoutHandler(
    IPayoutRequestRepository payouts,
    IUnitOfWork unitOfWork,
    ICallerContext caller,
    IClock clock,
    ILogger<RejectPayoutHandler> logger) : ICommandHandler<RejectPayoutCommand, PayoutRequestView>
{
    public async Task<PayoutRequestView> HandleAsync(
        RejectPayoutCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        PayoutReview.EnsureReviewer(caller);

        var demande = await payouts.GetByIdAsync(command.PayoutId, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException("Demande de versement", command.PayoutId.ToString());

        var signature = PayoutReview.Signature(caller);

        // LE MOTIF VIDE EST REFUSE PAR L'AGREGAT, pas ici : c'est une regle de
        // la demande, pas une precaution de ce handler, et elle doit tenir
        // quel que soit le chemin par lequel on arrive.
        demande.Reject(command.Reason, signature, clock.UtcNow);

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "Versement refuse : demande {PayoutId} de {AmountXof} F au livreur {DriverId}, par {Acteur}. Motif : {Motif}",
            demande.Id,
            demande.AmountXof,
            demande.DriverId,
            caller.ToActor().ToString(),
            demande.RejectionReason);

        return PayoutReview.ToView(demande);
    }
}
