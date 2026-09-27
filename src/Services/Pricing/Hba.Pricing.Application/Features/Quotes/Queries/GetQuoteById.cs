using Hba.BuildingBlocks.Application.Messaging;
using Hba.BuildingBlocks.Domain;
using Hba.Pricing.Application.Common.Interfaces;
using Hba.Pricing.Application.Common.Views;

namespace Hba.Pricing.Application.Features.Quotes.Queries;

/// <summary>
/// Relecture d'un devis deja emis. Aucune ecriture : un devis relu n'est ni
/// prolonge, ni recalcule. Si le prix a change entre-temps, c'est l'ancien qui
/// est rendu, avec sa date d'expiration — c'est precisement ce qu'on attend
/// d'un devis.
/// </summary>
public sealed record GetQuoteByIdQuery(Guid QuoteId) : IQuery<QuoteView>;

public sealed class GetQuoteByIdHandler(IQuoteRepository quotes)
    : IQueryHandler<GetQuoteByIdQuery, QuoteView>
{
    public async Task<QuoteView> HandleAsync(GetQuoteByIdQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var quote = await quotes.GetByIdAsync(query.QuoteId, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException("Devis", query.QuoteId.ToString());

        return QuoteView.From(quote);
    }
}
