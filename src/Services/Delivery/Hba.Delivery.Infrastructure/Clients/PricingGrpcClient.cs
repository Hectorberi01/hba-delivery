using Grpc.Core;
using Hba.BuildingBlocks.Domain;
using Hba.BuildingBlocks.Grpc;
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
        GeoPoint pickup,
        GeoPoint dropoff,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(pickup);
        ArgumentNullException.ThrowIfNull(dropoff);

        Quote quote;

        try
        {
            quote = await client.ConsumeQuoteAsync(
                new ConsumeQuoteRequest
                {
                    QuoteId = quoteId,
                    DeliveryId = deliveryId.ToString(),
                    Pickup = ToProto(pickup),
                    Dropoff = ToProto(dropoff),
                },
                cancellationToken: cancellationToken);
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound)
        {
            throw new NotFoundException("Devis", quoteId);
        }
        catch (RpcException ex) when (CodeMetier(ex) == "QUOTE_TRIP_MISMATCH")
        {
            // LE TRAJET NE CORRESPOND PAS, ET CE N'EST PAS « DEVIS INUTILISABLE ».
            //
            // Les deux sortent de Pricing en FAILED_PRECONDITION, mais ils ne
            // demandent pas la même chose au client : un devis expiré se
            // redemande tel quel, un trajet qui a changé exige de refaire la
            // saisie. Les confondre enverrait « réessayez » à quelqu'un dont
            // l'adresse de remise n'est plus celle qu'il a chiffrée — et
            // masquerait, dans les journaux, la seule trace d'une tentative de
            // payer une course longue au prix d'une courte.
            throw new DomainException(
                "QUOTE_TRIP_MISMATCH",
                "Le devis a été établi pour un autre trajet. Reprenez l'adresse pour obtenir un nouveau prix.");
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

    /// <summary>
    /// Le code metier voyage dans les trailers, pose par
    /// <c>ExceptionInterceptor</c>. C'est la seule facon de distinguer deux
    /// refus qui partagent le meme statut gRPC.
    /// </summary>
    private static string? CodeMetier(RpcException exception)
        => exception.Trailers.GetValue(GrpcMetadataKeys.ErrorCode);

    private static Contracts.Common.V1.GeoPoint ToProto(GeoPoint point)
        => new() { Latitude = point.Latitude, Longitude = point.Longitude };

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
