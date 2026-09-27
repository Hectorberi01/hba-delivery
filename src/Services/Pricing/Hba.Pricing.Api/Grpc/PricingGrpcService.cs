using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Hba.BuildingBlocks.Application.Messaging;
using Hba.BuildingBlocks.Domain;
using Hba.Contracts.Pricing.V1;
using Hba.Pricing.Application.Common.Views;
using Hba.Pricing.Application.Features.Quotes.Commands;
using Hba.Pricing.Application.Features.Quotes.Queries;
using Microsoft.AspNetCore.Authorization;
using CommonMoney = Hba.Contracts.Common.V1.Money;
using ProtoVehicleType = Hba.Contracts.Common.V1.VehicleType;
using DomainVehicleType = Hba.Pricing.Domain.Tariffs.VehicleType;

namespace Hba.Pricing.Api.Grpc;

/// <summary>
/// Entree synchrone du service Pricing.
///
/// PRICING EST LA SEULE AUTORITE SUR LE PRIX, comme le dit le contrat : ni le
/// client, ni le livreur, ni le commercant ne le fixent. Ce service ne prend
/// donc aucun montant en entree — seulement deux points, un vehicule et un
/// poids — et rend un devis qui sera consomme tel quel.
/// </summary>
[Authorize]
public sealed class PricingGrpcService(IDispatcher dispatcher) : PricingService.PricingServiceBase
{
    public override async Task<Quote> GetQuote(GetQuoteRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        if (request.Pickup is null || request.Dropoff is null)
        {
            throw new DomainException(
                "MISSING_POINTS",
                "Un devis demande un point d'enlevement et un point de remise.");
        }

        var view = await dispatcher.SendAsync(
            new GetQuoteCommand(
                request.Pickup.Latitude,
                request.Pickup.Longitude,
                request.Dropoff.Latitude,
                request.Dropoff.Longitude,
                ToDomain(request.VehicleType)),
            context.CancellationToken).ConfigureAwait(false);

        return ToProto(view);
    }

    public override async Task<Quote> GetQuoteById(GetQuoteByIdRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        var view = await dispatcher.QueryAsync(
            new GetQuoteByIdQuery(ParseId(request.QuoteId, "quote_id")),
            context.CancellationToken).ConfigureAwait(false);

        return ToProto(view);
    }

    /// <summary>
    /// Consomme le devis pour une livraison. Appele par Delivery au moment de
    /// la creation, JAMAIS par une application cliente : c'est le service qui
    /// cree la livraison qui fige le prix, pas celui qui l'affiche.
    /// </summary>
    public override async Task<Quote> ConsumeQuote(ConsumeQuoteRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        var view = await dispatcher.SendAsync(
            new ConsumeQuoteCommand(
                ParseId(request.QuoteId, "quote_id"),
                ParseId(request.DeliveryId, "delivery_id")),
            context.CancellationToken).ConfigureAwait(false);

        return ToProto(view);
    }

    /// <summary>
    /// Un identifiant mal forme est une erreur d'appelant, pas une panne : il
    /// devient INVALID_ARGUMENT et non une exception de parsing remontee en
    /// INTERNAL avec une reference de support.
    /// </summary>
    private static Guid ParseId(string value, string field)
        => Guid.TryParse(value, out var id) && id != Guid.Empty
            ? id
            : throw new DomainException("INVALID_ID", $"Le champ {field} n'est pas un identifiant valide.");

    /// <summary>
    /// Le vehicule non precise devient la moto.
    ///
    /// CE N'EST PAS UN DEFAUT ARBITRAIRE : a Cotonou la course se fait a moto,
    /// le zemidjan est le mode par defaut du marche, et c'est la grille la moins
    /// chere — un client qui ne precise rien ne se voit donc jamais facturer le
    /// tarif d'une camionnette.
    /// </summary>
    private static DomainVehicleType ToDomain(ProtoVehicleType vehicleType) => vehicleType switch
    {
        ProtoVehicleType.Car => DomainVehicleType.Car,
        ProtoVehicleType.Van => DomainVehicleType.Van,
        _ => DomainVehicleType.Motorcycle,
    };

    private static Quote ToProto(QuoteView view) => new()
    {
        Id = view.Id.ToString(),
        TariffVersion = view.TariffVersion,
        ZoneId = view.ZoneCode,
        Total = Money(view.Total),
        BaseFare = Money(view.BaseFare),
        DistanceFare = Money(view.DistanceFare),
        SurgeFare = Money(view.SurgeFare),
        DriverEarning = Money(view.DriverEarning),
        DistanceMeters = view.DistanceMeters,
        DurationSeconds = view.DurationSeconds,
        CreatedAt = Timestamp.FromDateTimeOffset(view.CreatedAt),
        ExpiresAt = Timestamp.FromDateTimeOffset(view.ExpiresAt),
        Consumed = view.Consumed,
    };

    private static CommonMoney Money(long amount) => new()
    {
        Amount = amount,
        Currency = Hba.Pricing.Domain.ValueObjects.MoneyXof.CurrencyCode,
    };
}
