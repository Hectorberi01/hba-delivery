using Hba.Pricing.Domain.Quotes;
using Hba.Pricing.Domain.Tariffs;
using Hba.Pricing.Domain.ValueObjects;

namespace Hba.Pricing.Application.Common.Interfaces;

/// <summary>
/// Zone tarifaire d'un point.
///
/// LE POLYGONE RESTE EN BASE, comme le dit l'agregat Zone : c'est la requete
/// qui fait la geometrie (ST_Contains), pas le domaine.
/// </summary>
public interface IZoneLocator
{
    /// <summary>Code de zone, ou null si le point n'est couvert par aucune.</summary>
    Task<string?> ResolveAsync(GeoPoint point, CancellationToken cancellationToken);
}

public interface ITariffRepository
{
    /// <summary>
    /// Grille en vigueur pour une zone, un vehicule et un instant.
    ///
    /// L'INSTANT N'EST PAS DECORATIF : une grille n'est jamais modifiee, on en
    /// cree une nouvelle version et on clot la precedente. Chercher « celle qui
    /// valait a cette date » est donc la seule lecture correcte.
    /// </summary>
    Task<Tariff?> FindInForceAsync(
        string zoneCode,
        VehicleType vehicleType,
        DateTimeOffset at,
        CancellationToken cancellationToken);

    void Add(Tariff tariff);

    Task<bool> AnyAsync(CancellationToken cancellationToken);
}

public interface IQuoteRepository
{
    Task<Quote?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    void Add(Quote quote);
}
