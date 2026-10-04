using Hba.BuildingBlocks.Application.Abstractions;
using Hba.BuildingBlocks.Application.Messaging;
using Hba.BuildingBlocks.Domain;
using Hba.BuildingBlocks.Security;
using Hba.Payment.Application.Common.Interfaces;
using Hba.Payment.Application.Common.Views;
using Hba.Payment.Domain.Earnings;
using Hba.Payment.Domain.Exceptions;
using Hba.Payment.Domain.Payments;
using Hba.Payment.Domain.ValueObjects;

namespace Hba.Payment.Application.Features.Payments.Commands;

/// <summary>
/// Ouvre le paiement d'un POURBOIRE, apres la mission (point 42, tranche le
/// 3 octobre 2026).
/// </summary>
///
/// <remarks>
/// C'EST UNE SECONDE PAGE DE PAIEMENT, ET C'EST LE PRIX ASSUME DE LA DECISION.
/// Aucun debit n'est possible sans le client, et la sollicitation directe est
/// refusee sur ce compte depuis le 3 octobre : un pourboire decide apres la
/// course est donc une transaction de plus, avec son operateur a choisir et son
/// code PIN a taper. Le point 42 accepte le taux d'abandon que cela produit.
///
/// LE MONTANT VIENT DU CLIENT, ET RIEN NE LE BORNE ICI. Pas de pourcentage, pas
/// de plafond : un plafond est une decision commerciale que personne n'a prise,
/// et l'inventer dans un handler la cacherait.
/// </remarks>
public sealed record CreateTipIntentCommand(
    string IdempotencyKey,
    ServiceType ServiceType,
    Guid ReferenceId,
    string PayerPhone,
    long AmountXof) : ICommand<PaymentIntentView>;

public sealed class CreateTipIntentHandler(
    IPaymentIntentRepository intents,
    IDriverLedgerRepository ledger,
    IPaymentProvider provider,
    IIdempotencyStore idempotency,
    IUnitOfWork unitOfWork,
    ICallerContext caller,
    IClock clock) : ICommandHandler<CreateTipIntentCommand, PaymentIntentView>
{
    private const string IdempotencyScope = "payment:create-tip";

    /// <summary>
    /// Duree de vie de l'intention. PLUS COURTE QUE CELLE D'UNE MISSION, et pour
    /// une raison simple : un client qui laisse un pourboire le fait dans la
    /// minute qui suit sa course, ou ne le fait pas. Une intention qui traine une
    /// demi-heure ne sert qu'a empecher d'en rouvrir une.
    /// </summary>
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    public async Task<PaymentIntentView> HandleAsync(
        CreateTipIntentCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.AmountXof <= 0)
        {
            throw new DomainException(
                PaymentErrorCodes.InvalidAmount,
                "Un pourboire porte sur un montant strictement positif.");
        }

        // LE REJEU REND LA MEME INTENTION. Un client qui tape deux fois ne doit
        // pas ouvrir deux transactions : il paierait deux pourboires.
        if (!string.IsNullOrWhiteSpace(command.IdempotencyKey))
        {
            var connue = await idempotency
                .TryGetResultAsync(IdempotencyScope, command.IdempotencyKey, cancellationToken)
                .ConfigureAwait(false);

            if (connue is not null && Guid.TryParse(connue, out var connuId))
            {
                var deja = await intents.GetByIdAsync(connuId, cancellationToken).ConfigureAwait(false)
                    ?? throw new NotFoundException("Intention de paiement", connue);

                return PaymentIntentView.From(deja);
            }
        }

        var active = await intents
            .FindActiveByReferenceAsync(
                command.ServiceType,
                command.ReferenceId,
                PaymentPurpose.Tip,
                cancellationToken)
            .ConfigureAwait(false);

        if (active is not null)
        {
            // UN SEUL POURBOIRE PAR MISSION, ET C'EST UN GARDE-FOU, PAS UNE REGLE
            // DE PRODUIT. Rien n'interdirait d'en laisser deux ; ce qui serait
            // grave, c'est qu'un client en paie deux sans l'avoir voulu — un
            // double appui, un retour en arriere, un ecran rouvert.
            return PaymentIntentView.From(active);
        }

        // LE LIVREUR VIENT DU GRAND LIVRE, PAS DE LA REQUETE.
        //
        // La remuneration de la mission y a ete inscrite au moment ou elle s'est
        // achevee, et elle porte le chauffeur qui l'a faite. Laisser l'application
        // nommer le beneficiaire, c'est permettre d'envoyer de l'argent a
        // n'importe qui.
        //
        // SON ABSENCE EST LE BON REFUS. Pas de ligne de remuneration veut dire
        // que la mission n'est pas allee au bout : on ne recompense pas une course
        // qui n'a pas eu lieu, et le message le dit au client au lieu d'ouvrir un
        // paiement qui n'aurait pas de destinataire.
        var remuneration = await ledger
            .FindMissionEntryAsync(
                LedgerEntryKind.DeliveryEarning,
                command.ServiceType,
                command.ReferenceId,
                cancellationToken)
            .ConfigureAwait(false);

        if (remuneration is null)
        {
            throw new DomainException(
                PaymentErrorCodes.MissingReference,
                "Cette mission n'est pas terminee : il n'y a pas encore de livreur a remercier.");
        }

        var now = clock.UtcNow;

        var intent = PaymentIntent.CreateTip(
            Guid.CreateVersion7(),
            command.ServiceType,
            command.ReferenceId,
            caller.SubjectId,
            command.PayerPhone,
            remuneration.DriverId,
            MoneyXof.FromPositive(command.AmountXof),
            now,
            Lifetime);

        var checkout = await provider
            .OpenCheckoutAsync(
                intent.Id,
                command.ServiceType,
                command.ReferenceId,
                intent.Amount,
                command.PayerPhone,
                cancellationToken)
            .ConfigureAwait(false);

        intent.AttachProviderCheckout(checkout.Reference, checkout.RedirectUrl);

        intents.Add(intent);

        if (!string.IsNullOrWhiteSpace(command.IdempotencyKey))
        {
            await idempotency
                .RememberAsync(IdempotencyScope, command.IdempotencyKey, intent.Id.ToString(), cancellationToken)
                .ConfigureAwait(false);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return PaymentIntentView.From(intent);
    }
}
