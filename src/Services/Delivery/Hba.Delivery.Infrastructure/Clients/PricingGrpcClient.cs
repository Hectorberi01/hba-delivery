using Grpc.Core;
using Hba.BuildingBlocks.Domain;
using Hba.Contracts.Pricing.V1;
using Hba.Delivery.Application.Ports;
using Hba.Delivery.Domain.ValueObjects;

namespace Hba.Delivery.Infrastructure.Clients;

/// <summary>
/// Adaptateur vers le service Pricing. Traduit le devis en snapshot figé et les
/// erreurs gRPC en exceptions de domaine, pour que la couche Application n'ait
/// jamais à connaître gRPC.
/// </summary>
internal sealed class PricingGrpcClient(PricingService.PricingServiceClient client) : IPricingClient
{
    public async Task<PricingSnapshot> ConsumeQuoteAsync(
        string quoteId,
        Guid deliveryId,
        CancellationToken cancellationToken)
    {
        Quote quote;

        try
        {
            quote = await client.ConsumeQuoteAsync(
                new ConsumeQuoteRequest { QuoteId = quoteId, DeliveryId = deliveryId.ToString() },
                cancellationToken: cancellationToken);
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound)
        {
            throw new NotFoundException("Devis", quoteId);
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.FailedPrecondition)
        {
            throw new DomainException("QUOTE_NOT_USABLE", "Le devis est expiré ou a déjà été consommé.");
        }

        return PricingSnapshot.Create(
            quote.Id,
            quote.TariffVersion,
            ToMoney(quote.Total),
            ToMoney(quote.BaseFare),
            ToMoney(quote.DistanceFare),
            ToMoney(quote.SurgeFare),
            ToMoney(quote.DriverEarning),
            quote.DistanceMeters,
            quote.DurationSeconds,
            quote.CreatedAt?.ToDateTimeOffset() ?? DateTimeOffset.UtcNow);
    }

    private static MoneyXof ToMoney(Contracts.Common.V1.Money? money)
    {
        if (money is null)
        {
            return MoneyXof.Zero;
        }

        if (!string.IsNullOrEmpty(money.Currency)
            && !string.Equals(money.Currency, MoneyXof.CurrencyCode, StringComparison.Ordinal))
        {
            throw new DomainException(
                "UNSUPPORTED_CURRENCY",
                $"Devise non prise en charge : {money.Currency}. Seul le XOF est accepté.");
        }

        return MoneyXof.From(money.Amount);
    }
}
