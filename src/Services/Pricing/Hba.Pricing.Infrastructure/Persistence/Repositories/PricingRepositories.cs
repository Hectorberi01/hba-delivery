using Hba.Pricing.Application.Common.Interfaces;
using Hba.Pricing.Domain.Quotes;
using Hba.Pricing.Domain.Tariffs;
using Microsoft.EntityFrameworkCore;

namespace Hba.Pricing.Infrastructure.Persistence.Repositories;

internal sealed class TariffRepository(PricingDbContext context) : ITariffRepository
{
    public Task<Tariff?> FindInForceAsync(
        string zoneCode,
        VehicleType vehicleType,
        DateTimeOffset at,
        CancellationToken cancellationToken)
        => context.Tariffs
            .Where(t => t.ZoneCode == zoneCode
                        && t.VehicleType == vehicleType
                        && t.ValidFrom <= at
                        && (t.ValidUntil == null || t.ValidUntil > at))
            // LA PLUS RECENTE GAGNE. Deux grilles qui se recouvrent sont une
            // anomalie de saisie, pas un cas metier : prendre la derniere entree
            // en vigueur est le comportement le moins surprenant, et il est
            // deterministe — contrairement a l'ordre de lecture en base.
            .OrderByDescending(t => t.ValidFrom)
            .FirstOrDefaultAsync(cancellationToken);

    public void Add(Tariff tariff) => context.Tariffs.Add(tariff);

    public Task<bool> AnyAsync(CancellationToken cancellationToken)
        => context.Tariffs.AnyAsync(cancellationToken);
}

internal sealed class QuoteRepository(PricingDbContext context) : IQuoteRepository
{
    public Task<Quote?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
        => context.Quotes.FirstOrDefaultAsync(q => q.Id == id, cancellationToken);

    public void Add(Quote quote) => context.Quotes.Add(quote);
}
