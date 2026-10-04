using Hba.Pricing.Application.Common.Interfaces;
using Hba.Pricing.Domain.Quotes;
using Hba.Pricing.Domain.Tariffs;
using Hba.Pricing.Domain.Zones;
using Microsoft.EntityFrameworkCore;

namespace Hba.Pricing.Infrastructure.Persistence.Repositories;

internal sealed class TariffRepository(PricingDbContext context) : ITariffRepository
{
    /// <summary>
    /// La grille qui s'applique.
    /// </summary>
    ///
    /// <remarks>
    /// LA PLAGE HORAIRE SE FILTRE EN MEMOIRE, PAS EN SQL, ET C'EST UN CHOIX.
    /// « Contient » doit traiter le cas d'une plage qui enjambe minuit — 22 h → 6 h
    /// s'ecrit avec un debut superieur a sa fin — et ecrire cette condition en LINQ
    /// traduit en SQL donnerait une expression que personne ne relit. La requete
    /// ramene les grilles d'une cle (zone, nature, vehicule) en vigueur a cet
    /// instant : elles sont deux ou trois, jamais plus.
    ///
    /// LA PLUS RECENTE GAGNE A EGALITE. Deux plages qui se recouvrent sont une
    /// anomalie de saisie que le controle de chevauchement refuse a la creation ; si
    /// l'une passait malgre tout, prendre la derniere entree en vigueur est le
    /// comportement le moins surprenant, et il est deterministe — contrairement a
    /// l'ordre de lecture en base.
    /// </remarks>
    public async Task<Tariff?> FindInForceAsync(
        string zoneCode,
        ServiceType serviceType,
        VehicleType vehicleType,
        DateTimeOffset at,
        TimeOnly heureLocale,
        CancellationToken cancellationToken)
    {
        var candidates = await FindInForceOnKeyAsync(zoneCode, serviceType, vehicleType, at, cancellationToken)
            .ConfigureAwait(false);

        return candidates.FirstOrDefault(t => t.AppliesAt(heureLocale));
    }

    public async Task<IReadOnlyList<Tariff>> FindInForceOnKeyAsync(
        string zoneCode,
        ServiceType serviceType,
        VehicleType vehicleType,
        DateTimeOffset at,
        CancellationToken cancellationToken)
        => await context.Tariffs
            .Where(t => t.ZoneCode == zoneCode
                        && t.ServiceType == serviceType
                        && t.VehicleType == vehicleType
                        && t.ValidFrom <= at
                        && (t.ValidUntil == null || t.ValidUntil > at))
            .OrderByDescending(t => t.ValidFrom)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public void Add(Tariff tariff) => context.Tariffs.Add(tariff);

    public Task<bool> AnyAsync(CancellationToken cancellationToken)
        => context.Tariffs.AnyAsync(cancellationToken);

    // PAS DE AsNoTracking ICI, CONTRAIREMENT A ListAsync : cette grille est lue
    // pour etre CLOSE. Detachee du contexte, Close() modifierait un objet que
    // SaveChanges ne regarde pas, et la cloture serait perdue sans une seule
    // erreur.
    public Task<Tariff?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
        => context.Tariffs.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Tariff>> ListAsync(
        string? zoneCode,
        ServiceType? serviceType,
        VehicleType? vehicleType,
        bool includeClosed,
        DateTimeOffset at,
        CancellationToken cancellationToken)
    {
        var requete = context.Tariffs.AsNoTracking().AsQueryable();

        // LE CODE DE ZONE EST NORMALISE A L'ECRITURE (Tariff.Create le passe en
        // minuscules) : le filtre doit l'etre aussi, sinon « COTONOU » ne
        // trouverait jamais « cotonou ».
        if (!string.IsNullOrWhiteSpace(zoneCode))
        {
            var code = zoneCode.Trim().ToLowerInvariant();
            requete = requete.Where(t => t.ZoneCode == code);
        }

        // PAS DE COMPARAISON A « Unspecified » : cet enum de domaine n'en a pas.
        // La valeur zero du contrat est traduite en nul par la couche Api, ce
        // qui est la seule frontiere ou elle a un sens.
        if (vehicleType is not null)
        {
            requete = requete.Where(t => t.VehicleType == vehicleType);
        }

        // MEME RAISON QUE POUR LE VEHICULE : le domaine n'a pas de valeur
        // « non precisee » pour le filtre, et la valeur zero du contrat est traduite en
        // nul par la couche Api — la seule frontiere ou elle a un sens.
        if (serviceType is not null)
        {
            requete = requete.Where(t => t.ServiceType == serviceType);
        }

        if (!includeClosed)
        {
            requete = requete.Where(t => t.ValidFrom <= at
                                         && (t.ValidUntil == null || t.ValidUntil > at));
        }

        return await requete
            .OrderBy(t => t.ZoneCode)
            .ThenBy(t => t.ServiceType)
            .ThenBy(t => t.VehicleType)
            .ThenByDescending(t => t.ValidFrom)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}

internal sealed class QuoteRepository(PricingDbContext context) : IQuoteRepository
{
    public Task<Quote?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
        => context.Quotes.FirstOrDefaultAsync(q => q.Id == id, cancellationToken);

    public void Add(Quote quote) => context.Quotes.Add(quote);
}


/// <summary>
/// Les zones, sans leur contour : celui-ci vit dans PostgisZoneStore.
/// </summary>
///
/// <remarks>
/// AUCUNE REQUETE GEOGRAPHIQUE ICI, ET C'EST LA FRONTIERE QUI COMPTE. Ce depot ne
/// connait que des lignes de table ; la colonne « area » n'est meme pas dans le
/// modele EF. Celui qui cherche « quelle zone contient ce point » passe par
/// IZoneLocator, pas par ici.
/// </remarks>
internal sealed class ZoneRepository(PricingDbContext context) : IZoneRepository
{
    public Task<Zone?> GetByCodeAsync(string code, CancellationToken cancellationToken)
        => context.Zones.FirstOrDefaultAsync(z => z.Code == code, cancellationToken);

    /// <summary>
    /// PAR PRIORITE DECROISSANTE, COMME LA RECHERCHE. L'ecran lit donc les zones
    /// dans l'ordre ou elles se departagent : celle qui gagne un chevauchement est
    /// au-dessus de celle qu'elle recouvre, et la carte se relit sans reflechir.
    /// </summary>
    public async Task<IReadOnlyList<Zone>> ListAsync(
        bool includeInactive,
        CancellationToken cancellationToken)
        => await context.Zones
            .Where(z => includeInactive || z.IsActive)
            .OrderByDescending(z => z.Priority)
            .ThenBy(z => z.Code)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public void Add(Zone zone) => context.Zones.Add(zone);
}
