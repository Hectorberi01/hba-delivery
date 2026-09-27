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
/// Ouverture d'un paiement pour une livraison.
///
/// LE MONTANT VIENT DE L'APPELANT ET C'EST ASSUME : Delivery a fige le devis
/// aupres de Pricing avant d'appeler ici, et c'est ce montant fige qu'il
/// transmet. Payment ne recalcule pas un prix — il n'a ni la grille, ni la
/// distance, ni le droit (ADR 0004 : Pricing est la seule autorite sur le prix).
/// </summary>
public sealed record CreatePaymentIntentCommand(
    string IdempotencyKey,
    Guid DeliveryId,
    string PayerId,
    string PayerPhone,
    long Amount,
    PaymentMethod Method,
    string? ReturnUrl) : ICommand<PaymentIntentView>;

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
    /// Duree de vie indicative de la page de paiement. Elle ne sert jamais a
    /// refuser un encaissement tardif : voir <see cref="PaymentIntent.ExpiresAt"/>.
    /// </summary>
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(30);

    public async Task<PaymentIntentView> HandleAsync(
        CreatePaymentIntentCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        EnsureCallerMayPayFor(command.PayerId);

        // Rejeu explicite : la meme cle rend la meme intention, et donc la meme
        // adresse de paiement. Le client qui reessaie ne se voit pas facturer
        // deux fois.
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

        // Filet de securite quand l'appelant n'a pas fourni de cle : une
        // livraison n'a qu'une intention vivante. Sans ce garde-fou, deux
        // appels concurrents ouvriraient deux transactions payables.
        var active = await intents.FindActiveByDeliveryAsync(command.DeliveryId, cancellationToken)
            .ConfigureAwait(false);

        if (active is not null)
        {
            return PaymentIntentView.From(active);
        }

        var now = clock.UtcNow;

        var intent = PaymentIntent.Create(
            Guid.CreateVersion7(),
            command.DeliveryId,
            command.PayerId,
            command.PayerPhone,
            MoneyXof.FromPositive(command.Amount),
            command.Method,
            now,
            Lifetime);

        // L'APPEL SORTANT PRECEDE LE COMMIT, ET CE N'EST PAS ANODIN. Si
        // l'enregistrement echoue ensuite, une transaction reste ouverte chez le
        // fournisseur sans intention en face. C'est le moindre mal : l'inverse —
        // enregistrer d'abord — laisserait une intention sans adresse de
        // paiement, donc un client bloque devant un ecran vide. Une transaction
        // orpheline, elle, n'est jamais payee et se solde d'elle-meme.
        var checkout = await provider
            .OpenCheckoutAsync(
                intent.Id,
                command.DeliveryId,
                intent.Amount,
                command.PayerPhone,
                command.ReturnUrl,
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
