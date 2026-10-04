using Hba.BuildingBlocks.Application.Abstractions;
using Hba.BuildingBlocks.Application.Messaging;
using Hba.BuildingBlocks.Domain;
using Hba.BuildingBlocks.Security;
using Hba.Payment.Application.Common.Interfaces;
using Hba.Payment.Application.Common.Views;
using Hba.Payment.Domain.Payments;
using Hba.Payment.Domain.ValueObjects;

namespace Hba.Payment.Application.Features.Payments.Commands;

/// <summary>
/// Ouverture d'un paiement pour une MISSION : une livraison, ou une course.
///
/// LE MONTANT VIENT DE L'APPELANT ET C'EST ASSUME : Delivery a fige le devis
/// aupres de Pricing avant d'appeler ici, et c'est ce montant fige qu'il
/// transmet. Payment ne recalcule pas un prix — il n'a ni la grille, ni la
/// distance, ni le droit (ADR 0004 : Pricing est la seule autorite sur le prix).
/// </summary>
public sealed record CreatePaymentIntentCommand(
    string IdempotencyKey,
    ServiceType ServiceType,
    Guid ReferenceId,
    string PayerId,
    string PayerPhone,
    long Amount,
    PaymentMethod Method) : ICommand<PaymentIntentView>;

public sealed class CreatePaymentIntentHandler(
    IPaymentIntentRepository intents,
    IPaymentProvider provider,
    IIdempotencyStore idempotency,
    IUnitOfWork unitOfWork,
    ICallerContext caller,
    IClock clock) : ICommandHandler<CreatePaymentIntentCommand, PaymentIntentView>
{
    private const string IdempotencyScope = "payment:create-intent";

    /// <summary>
    /// Duree de vie indicative de l'intention. Elle ne sert jamais a refuser un
    /// encaissement tardif : voir <see cref="PaymentIntent.ExpiresAt"/>.
    /// </summary>
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(30);

    public async Task<PaymentIntentView> HandleAsync(
        CreatePaymentIntentCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        EnsureCallerMayPayFor(command.PayerId);

        // Rejeu explicite : la meme cle rend la meme intention, et AUCUNE
        // seconde transaction n'est ouverte chez le fournisseur. Le client qui
        // reessaie retombe sur la meme page de paiement, et ne se voit donc pas
        // facturer deux fois la meme mission.
        if (!string.IsNullOrWhiteSpace(command.IdempotencyKey))
        {
            var known = await idempotency
                .TryGetResultAsync(IdempotencyScope, command.IdempotencyKey, cancellationToken)
                .ConfigureAwait(false);

            if (known is not null && Guid.TryParse(known, out var knownId))
            {
                var existing = await intents.GetByIdAsync(knownId, cancellationToken).ConfigureAwait(false)
                    ?? throw new NotFoundException("Intention de paiement", known);

                return PaymentIntentView.From(existing);
            }
        }

        // Filet de securite quand l'appelant n'a pas fourni de cle : une mission
        // n'a qu'une intention vivante. Sans ce garde-fou, deux appels concurrents
        // ouvriraient deux transactions payables.
        var active = await intents
            .FindActiveByReferenceAsync(
                command.ServiceType,
                command.ReferenceId,
                PaymentPurpose.Mission,
                cancellationToken)
            .ConfigureAwait(false);

        if (active is not null)
        {
            return PaymentIntentView.From(active);
        }

        var now = clock.UtcNow;

        var intent = PaymentIntent.Create(
            Guid.CreateVersion7(),
            command.ServiceType,
            command.ReferenceId,
            command.PayerId,
            command.PayerPhone,
            MoneyXof.FromPositive(command.Amount),
            command.Method,
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

    /// <summary>
    /// Verification cote service, jamais cote appelant (ADR 0007). Un client ne
    /// declenche pas un paiement au nom d'un autre : le jeton propage dit qui
    /// il est, et ce qu'il paie doit lui correspondre.
    /// </summary>
    private void EnsureCallerMayPayFor(string payerId)
    {
        if (!caller.IsInRole(HbaRoles.Customer))
        {
            return;
        }

        if (!string.Equals(caller.SubjectId, payerId, StringComparison.Ordinal))
        {
            throw new ForbiddenException("Un client ne peut pas ouvrir un paiement au nom d'un autre.");
        }
    }
}
