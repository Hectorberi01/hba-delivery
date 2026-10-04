using Hba.Pricing.Domain.ValueObjects;
using Hba.Pricing.Domain.Zones;

namespace Hba.Pricing.Application.Common.Views;

/// <summary>
/// Une zone tarifaire, telle qu'on la montre et qu'on la dessine.
/// </summary>
///
/// <remarks>
/// LE CONTOUR NE VIENT PAS DE L'AGREGAT, ET IL NE PEUT PAS EN VENIR. Le polygone
/// vit en base dans une colonne geography et le domaine ne fait pas de geometrie
/// (voir <see cref="Zone"/>) : c'est la vue qui reunit les deux, parce qu'un
/// ecran a besoin des deux a la fois.
///
/// VIDE SIGNIFIE « PAS ENCORE DESSINEE ». Une zone sans contour ne contient aucun
/// point, donc ne s'applique a personne : c'est le comportement sur, et il vaut
/// mieux qu'une zone qui attraperait tout.
/// </remarks>
public sealed record ZoneView(
    Guid Id,
    string Code,
    string Name,
    int Priority,
    bool Active,
    IReadOnlyList<GeoPoint> Ring)
{
    public static ZoneView From(Zone zone, IReadOnlyList<GeoPoint> ring)
    {
        ArgumentNullException.ThrowIfNull(zone);

        return new ZoneView(zone.Id, zone.Code, zone.Name, zone.Priority, zone.IsActive, ring);
    }
}
