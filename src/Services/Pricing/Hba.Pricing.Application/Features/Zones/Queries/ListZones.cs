using Hba.BuildingBlocks.Application.Abstractions;
using Hba.BuildingBlocks.Application.Messaging;
using Hba.Pricing.Application.Authorization;
using Hba.Pricing.Application.Common.Interfaces;
using Hba.Pricing.Application.Common.Views;

namespace Hba.Pricing.Application.Features.Zones.Queries;

/// <summary>
/// Les zones, avec leur contour.
/// </summary>
///
/// <remarks>
/// LA LECTURE EST PLUS LARGE QUE L'ECRITURE, et c'est deliberé (point 40). La
/// carte des zones est de la supervision : ops en a besoin pour comprendre
/// pourquoi une course a ete chiffree a ce prix-la, et pourquoi tel livreur a ete
/// cherche dans tel perimetre. Dessiner, en revanche, deplace des prix.
///
/// LES CONTOURS SE LISENT EN UN SEUL APPEL, pas un par zone. Une ville en compte
/// une dizaine ; les demander une a une ferait dix allers-retours pour afficher
/// une carte.
/// </remarks>
public sealed record ListZonesQuery(bool IncludeInactive) : IQuery<IReadOnlyList<ZoneView>>;

public sealed class ListZonesHandler(
    IZoneRepository zones,
    IZoneRingStore contours,
    ICallerContext caller) : IQueryHandler<ListZonesQuery, IReadOnlyList<ZoneView>>
{
    public async Task<IReadOnlyList<ZoneView>> HandleAsync(
        ListZonesQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        PricingAccess.EnsureCanReadZones(caller);

        var trouvees = await zones.ListAsync(query.IncludeInactive, cancellationToken).ConfigureAwait(false);

        if (trouvees.Count == 0)
        {
            return [];
        }

        var traces = await contours
            .ReadAsync([.. trouvees.Select(z => z.Id)], cancellationToken)
            .ConfigureAwait(false);

        return
        [
            .. trouvees.Select(zone =>
                ZoneView.From(zone, traces.TryGetValue(zone.Id, out var ring) ? ring : [])),
        ];
    }
}
