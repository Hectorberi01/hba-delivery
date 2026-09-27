using Hba.BuildingBlocks.Application.Abstractions;
using Hba.BuildingBlocks.Application.Messaging;
using Hba.BuildingBlocks.Application.Time;
using Hba.BuildingBlocks.Domain;
using Hba.BuildingBlocks.Security;
using Hba.Delivery.Application.Ports;

namespace Hba.Delivery.Application.Queries;

public sealed record GetDeliveryStatsQuery(TimeWindow Window, TimeGranularity Granularity)
    : IQuery<DeliveryStatsView>;

public sealed class GetDeliveryStatsHandler(
    IDeliveryStatsReader lecteur,
    ITimeCalendar calendrier,
    ICallerContext caller) : IQueryHandler<GetDeliveryStatsQuery, DeliveryStatsView>
{
    public async Task<DeliveryStatsView> HandleAsync(
        GetDeliveryStatsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        // VERIFIE ICI, PAS A LA PASSERELLE (ADR 0007). Ces chiffres agrègent
        // toute l'activité de la plateforme : un client ou un livreur n'a
        // rien à y voir, et un jeton de service — sans rôle — non plus.
        if (!caller.Roles.Overlaps(HbaRoles.BackOffice))
        {
            throw new ForbiddenException("Les indicateurs sont réservés au back-office.");
        }

        var fenetre = calendrier.Validate(query.Window);
        var vue = await lecteur.ReadAsync(fenetre, query.Granularity, cancellationToken).ConfigureAwait(false);

        // LES PERIODES VIDES SONT REINTRODUITES ICI, pas en SQL. Un GROUP BY
        // ne rend aucune ligne pour un jour sans course : sans ce recollement,
        // le graphique resserre ses barres et un creux disparaît au lieu de
        // s'afficher à zéro.
        var mesures = vue.CreatedSeries.ToDictionary(p => p.Key, p => p.Count, StringComparer.Ordinal);

        var serie = calendrier
            .Keys(fenetre, query.Granularity)
            .Select(cle => new SeriesTally(cle, mesures.TryGetValue(cle, out var valeur) ? valeur : 0))
            .ToList();

        return vue with { CreatedSeries = serie };
    }
}
