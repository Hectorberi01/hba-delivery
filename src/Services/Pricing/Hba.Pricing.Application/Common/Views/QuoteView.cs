using Hba.Pricing.Domain.Quotes;
using Hba.Pricing.Domain.Tariffs;

namespace Hba.Pricing.Application.Common.Views;

/// <summary>
/// Devis tel qu'il sort du service. Les montants sont des entiers de francs
/// CFA : le XOF n'a pas de subdivision, et l'ADR 0006 interdit les decimaux.
/// </summary>
///
/// <remarks>
/// LA NATURE SORT AVEC LE DEVIS parce que c'est elle qui sera verifiee a la
/// consommation : Pricing refuse de consommer un devis de colis pour une course.
/// L'appelant la relit donc ici plutot que de se souvenir de ce qu'il a demande.
/// </remarks>
public sealed record QuoteView(
    Guid Id,
    string TariffVersion,
    string ZoneCode,
    ServiceType ServiceType,
    long Total,
    long BaseFare,
    long DistanceFare,
    long SurgeFare,
    long DriverEarning,
    long CancellationFee,
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
            quote.ServiceType,
            quote.Total.Amount,
            quote.BaseFare.Amount,
            quote.VariableFare.Amount,
            quote.SurgeFare.Amount,
            quote.DriverEarning.Amount,
            quote.CancellationFee.Amount,
            quote.Route.DistanceMeters,
            quote.Route.DurationSeconds,
            quote.CreatedAt,
            quote.ExpiresAt,
            quote.IsConsumed);
    }
}
