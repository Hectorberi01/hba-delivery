using Hba.BuildingBlocks.Application.Abstractions;
using Hba.BuildingBlocks.Application.Messaging;
using Hba.BuildingBlocks.Domain;
using Hba.Pricing.Application.Common.Interfaces;
using Hba.Pricing.Application.Common.Views;

namespace Hba.Pricing.Application.Features.Quotes.Commands;

/// <summary>
/// Consommation d'un devis au profit d'une livraison.
///
/// C'EST LE POINT OU LE PRIX CESSE D'ETRE UNE PROPOSITION. Tant que le devis
/// n'est pas consomme, il n'engage personne et expire seul ; une fois consomme,
/// il est recopie dans la livraison et ne bougera plus (ADR 0004). La regle de
/// consommation elle-meme — un seul preneur, pas apres expiration, rejouable
/// pour la MEME livraison — appartient a l'agregat <see cref="Domain.Quotes.Quote"/>
/// et n'est pas redite ici : ce handler charge, delegue, enregistre.
/// </summary>
public sealed record ConsumeQuoteCommand(Guid QuoteId, Guid DeliveryId) : ICommand<QuoteView>;

public sealed class ConsumeQuoteHandler(
    IQuoteRepository quotes,
    IUnitOfWork unitOfWork,
    IClock clock) : ICommandHandler<ConsumeQuoteCommand, QuoteView>
{
    public async Task<QuoteView> HandleAsync(ConsumeQuoteCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var quote = await quotes.GetByIdAsync(command.QuoteId, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException("Devis", command.QuoteId.ToString());

        quote.Consume(command.DeliveryId, clock.UtcNow);

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return QuoteView.From(quote);
    }
}
