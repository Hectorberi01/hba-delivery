using Hba.Pricing.Domain.Quotes;

namespace Hba.Pricing.Application.Common.Views;

/// <summary>
/// Devis tel qu'il sort du service. Les montants sont des entiers de francs
/// CFA : le XOF n'a pas de subdivision, et l'ADR 0006 interdit les decimaux.
/// </summary>
public sealed record QuoteView(
    Guid Id,
    string TariffVersion,
    string ZoneCode,
    long Total,
    long BaseFare,
    long DistanceFare,
    long SurgeFare,
    long DriverEarning,
    int DistanceMeters,
    int DurationSeconds,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    bool Consumed)
{
    public static QuoteView From(Quote quote)
    {
        ArgumentNullException.ThrowIfNull(quote);

        return new QuoteView(
            quote.Id,
            quote.TariffVersion,
            quote.ZoneCode,
            quote.Total.Amount,
            quote.BaseFare.Amount,
            quote.VariableFare.Amount,
            quote.SurgeFare.Amount,
            quote.DriverEarning.Amount,
            quote.Route.DistanceMeters,
            quote.Route.DurationSeconds,
            quote.CreatedAt,
            quote.ExpiresAt,
            quote.IsConsumed);
    }
}
