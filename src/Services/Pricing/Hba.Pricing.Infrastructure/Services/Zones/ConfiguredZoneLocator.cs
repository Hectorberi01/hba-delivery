using Hba.Pricing.Application.Common.Interfaces;
using Hba.Pricing.Domain.ValueObjects;
using Microsoft.Extensions.Options;

namespace Hba.Pricing.Infrastructure.Services.Zones;

public sealed class ZoneOptions
{
    public const string SectionName = "Zones";

    /// <summary>
    /// Zone appliquee a TOUT point, tant qu'aucun polygone n'est dessine.
    ///
    /// VIDE SIGNIFIE « AUCUNE ZONE », donc aucun prix : c'est le comportement
    /// correct en production, ou livrer hors zone n'a pas de sens. Une valeur
    /// non vide fait entrer le monde entier dans cette zone, ce qui n'est
    /// acceptable qu'en developpement.
    /// </summary>
    public string? FallbackZoneCode { get; set; }
}

/// <summary>
/// Resolution de zone provisoire.
///
/// LA VRAIE VERSION INTERROGERA POSTGIS. L'agregat Zone le dit : le polygone
/// vit en base, en colonne geography, et seule la requete le manipule
/// (ST_Contains), departageant les chevauchements par priorite decroissante.
/// Cette implementation ne lit aucun polygone — il n'y en a aucun de dessine —
/// et se contente de la zone de repli configuree.
/// </summary>
internal sealed class ConfiguredZoneLocator(IOptions<ZoneOptions> options) : IZoneLocator
{
    public Task<string?> ResolveAsync(GeoPoint point, CancellationToken cancellationToken)
    {
        var fallback = options.Value.FallbackZoneCode;

        return Task.FromResult(string.IsNullOrWhiteSpace(fallback) ? null : fallback);
    }
}
