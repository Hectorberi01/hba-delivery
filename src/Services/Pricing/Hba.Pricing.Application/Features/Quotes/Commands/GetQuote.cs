using Hba.BuildingBlocks.Application.Abstractions;
using Hba.BuildingBlocks.Application.Messaging;
using Hba.BuildingBlocks.Domain;
using Hba.Pricing.Application.Common.Interfaces;
using Hba.Pricing.Application.Common.Views;
using Hba.Pricing.Domain.Quotes;
using Hba.Pricing.Domain.Tariffs;
using Hba.Pricing.Domain.ValueObjects;

namespace Hba.Pricing.Application.Features.Quotes.Commands;

/// <summary>
/// Demande de prix.
///
/// C'EST UNE COMMANDE, PAS UNE REQUETE, et la nuance n'est pas doctrinale : un
/// devis est ECRIT. Il porte un identifiant, une date d'expiration, et il sera
/// consomme plus tard par la livraison qui s'en reclame (ADR 0004). Le rendre
/// sans le persister reviendrait a promettre un prix que plus personne ne
/// pourrait retrouver.
/// </summary>
public sealed record GetQuoteCommand(
    double PickupLatitude,
    double PickupLongitude,
    double DropoffLatitude,
    double DropoffLongitude,
    VehicleType VehicleType) : ICommand<QuoteView>;

public sealed class GetQuoteHandler(
    IZoneLocator zones,
    ITariffRepository tariffs,
    IQuoteRepository quotes,
    IRouteEngine routes,
    IUnitOfWork unitOfWork,
    IClock clock) : ICommandHandler<GetQuoteCommand, QuoteView>
{
    /// <summary>
    /// Duree de validite d'un devis. Assez long pour remplir un formulaire,
    /// assez court pour qu'un prix affiche reste celui qu'on facturera.
    /// </summary>
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(15);

    public async Task<QuoteView> HandleAsync(GetQuoteCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var pickup = GeoPoint.Create(command.PickupLatitude, command.PickupLongitude);
        var dropoff = GeoPoint.Create(command.DropoffLatitude, command.DropoffLongitude);

        var zoneCode = await zones.ResolveAsync(pickup, cancellationToken).ConfigureAwait(false)
            ?? throw new DomainException(
                "ZONE_NOT_COVERED",
                "Aucune zone de livraison ne couvre le point d'enlevement.");

        var now = clock.UtcNow;

        var tariff = await tariffs.FindInForceAsync(zoneCode, command.VehicleType, now, cancellationToken)
                .ConfigureAwait(false)
            ?? throw new DomainException(
                "NO_TARIFF",
                $"Aucune grille tarifaire en vigueur pour la zone {zoneCode}.");

        var route = await routes.MeasureAsync(pickup, dropoff, cancellationToken).ConfigureAwait(false);

        var quote = Quote.Create(Guid.CreateVersion7(), tariff, pickup, dropoff, route, now, Lifetime);

        quotes.Add(quote);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return QuoteView.From(quote);
    }
}
