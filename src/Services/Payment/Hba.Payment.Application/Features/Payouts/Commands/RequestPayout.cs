using Hba.BuildingBlocks.Application.Abstractions;
using Hba.BuildingBlocks.Application.Messaging;
using Hba.BuildingBlocks.Domain;
using Hba.BuildingBlocks.Security;
using Hba.Payment.Application.Common.Interfaces;
using Hba.Payment.Domain.Exceptions;
using Hba.Payment.Domain.Payouts;
using Microsoft.Extensions.Options;

namespace Hba.Payment.Application.Features.Payouts.Commands;

/// <summary>
/// Un livreur demande a etre paye.
///
/// AUCUN ARGENT NE BOUGE ICI. La demande est une trace : la finance la lit,
/// l'approuve ou la refuse, fait le virement ailleurs, puis vient consigner
/// sa reference. Tant que ce dernier geste n'a pas eu lieu, le grand livre du
/// livreur ne bouge pas non plus — c'est ce qui garantit que « verse »
/// signifie « recu ».
///
/// SEUL LE LIVREUR DEMANDE POUR LUI-MEME. Ni finance ni admin ne peuvent
/// ouvrir une demande a sa place : une demande est un acte de volonte, et la
/// faire a la place de quelqu'un brouille la seule chose qu'elle prouve.
/// </summary>
public sealed record RequestPayoutCommand(long AmountXof) : ICommand<PayoutRequestView>;

public sealed class RequestPayoutHandler(
    IPayoutRequestRepository payouts,
    IDriverLedgerRepository ledger,
    IUnitOfWork unitOfWork,
    IOptions<PayoutOptions> options,
    ICallerContext caller,
    IClock clock) : ICommandHandler<RequestPayoutCommand, PayoutRequestView>
{
    private readonly PayoutOptions _options = options.Value;

    public async Task<PayoutRequestView> HandleAsync(
        RequestPayoutCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var driverId = caller.DriverId;

        if (!caller.IsInRole(HbaRoles.Driver) || string.IsNullOrWhiteSpace(driverId))
        {
            throw new ForbiddenException("Seul un livreur demande un versement, et seulement pour lui-meme.");
        }

        // UNE SEULE DEMANDE EN COURS. Le controle est ici pour rendre un
        // refus comprehensible ; l'index unique en base le garantit quand
        // deux requetes arrivent ensemble.
        var enCours = await payouts.FindPendingAsync(driverId, cancellationToken).ConfigureAwait(false);

        if (enCours is not null)
        {
            throw new DomainException(
                PaymentErrorCodes.PayoutAlreadyPending,
                "Une demande de versement est deja en cours. Attendez sa reponse avant d'en ouvrir une autre.");
        }

        if (_options.MinimumXof > 0 && command.AmountXof < _options.MinimumXof)
        {
            throw new DomainException(
                PaymentErrorCodes.PayoutBelowMinimum,
                $"Le montant minimum d'un versement est de {_options.MinimumXof} F.");
        }

        // LA CARENCE SE LIT SUR LA DATE DES COURSES, PAS SUR CELLE DE LA
        // DEMANDE. Ce qui compte est l'age de chaque remuneration : une course
        // livree il y a dix minutes n'est pas retirable, une course d'hier
        // l'est. A zero heure, cette borne vaut « maintenant » et tout compte.
        var borne = clock.UtcNow.AddHours(-Math.Max(_options.CoolingOffHours, 0));

        var disponible = await ledger
            .DueForPayoutAsync(driverId, borne, cancellationToken)
            .ConfigureAwait(false);

        if (command.AmountXof > disponible)
        {
            // LE MESSAGE DIT LE DISPONIBLE, PAS SEULEMENT LE REFUS. Un livreur
            // a qui l'on repond « trop eleve » sans dire combien redemandera
            // au hasard.
            throw new DomainException(
                PaymentErrorCodes.PayoutExceedsBalance,
                _options.CoolingOffHours > 0
                    ? $"Montant disponible : {disponible} F. Les courses de moins de {_options.CoolingOffHours} h n'y sont pas encore comptees."
                    : $"Montant disponible : {disponible} F.");
        }

        var demande = PayoutRequest.Open(Guid.CreateVersion7(), driverId, command.AmountXof, clock.UtcNow);

        payouts.Add(demande);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new PayoutRequestView(
            demande.Id,
            demande.DriverId,
            demande.AmountXof,
            demande.Status,
            demande.RequestedAt,
            demande.DecidedAt,
            demande.DecidedBy,
            demande.RejectionReason,
            demande.PaidAt,
            demande.PaymentReference);
    }
}
