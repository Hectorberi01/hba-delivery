using Hba.BuildingBlocks.Application.Abstractions;
using Hba.BuildingBlocks.Application.Messaging;
using Hba.BuildingBlocks.Domain;
using Hba.Pricing.Application.Common.Interfaces;
using Hba.Pricing.Application.Common.Views;
using Hba.Pricing.Domain.Tariffs;
using Hba.Pricing.Domain.ValueObjects;

namespace Hba.Pricing.Application.Features.Quotes.Commands;

/// <summary>
/// Consommation d'un devis au profit d'une livraison.
///
/// C'EST LE POINT OU LE PRIX CESSE D'ETRE UNE PROPOSITION. Tant que le devis
/// n'est pas consomme, il n'engage personne et expire seul ; une fois consomme,
/// il est recopie dans la livraison et ne bougera plus (ADR 0004). La regle de
/// consommation elle-meme — un seul preneur, pas apres expiration, rejouable
/// pour la MEME livraison, ET POUR LE TRAJET CHIFFRE — appartient a l'agregat
/// <see cref="Domain.Quotes.Quote"/> et n'est pas redite ici : ce handler charge,
/// delegue, enregistre.
/// </summary>
/// <remarks>
/// LE TRAJET FAIT PARTIE DE LA DEMANDE DEPUIS LE 30 SEPTEMBRE 2026. Sans lui,
/// Delivery pouvait consommer un devis court pour une course longue et encaisser
/// le prix du court : le devis portait bien ses points, personne ne les
/// comparait. Les quatre coordonnees sont celles de la livraison en cours de
/// creation ; l'agregat tranche.
/// </remarks>
public sealed record ConsumeQuoteCommand(
    Guid QuoteId,
    Guid DeliveryId,

    /// <summary>
    /// La nature de la prestation qui consomme le devis.
    /// </summary>
    ///
    /// <remarks>
    /// ELLE EST VERIFIEE PAR L'AGREGAT, pour la meme raison que le trajet : un devis
    /// de colis est moins cher qu'un devis de course sur la meme distance, et sans ce
    /// controle un telephone pourrait payer une course au prix d'un paquet.
    /// </remarks>
    ServiceType ServiceType,
    double PickupLatitude,
    double PickupLongitude,
    double DropoffLatitude,
    double DropoffLongitude) : ICommand<QuoteView>;

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

        quote.Consume(
            command.DeliveryId,
            command.ServiceType,
            GeoPoint.Create(command.PickupLatitude, command.PickupLongitude),
            GeoPoint.Create(command.DropoffLatitude, command.DropoffLongitude),
            clock.UtcNow);

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return QuoteView.From(quote);
    }
}
