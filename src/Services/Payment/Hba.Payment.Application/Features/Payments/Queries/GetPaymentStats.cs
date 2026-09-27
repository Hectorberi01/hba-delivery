using Hba.BuildingBlocks.Application.Abstractions;
using Hba.BuildingBlocks.Application.Messaging;
using Hba.BuildingBlocks.Application.Time;
using Hba.BuildingBlocks.Domain;
using Hba.BuildingBlocks.Security;
using Hba.Payment.Application.Common.Interfaces;

namespace Hba.Payment.Application.Features.Payments.Queries;

public sealed record GetPaymentStatsQuery(TimeWindow Window, TimeGranularity Granularity)
    : IQuery<PaymentStatsView>;

public sealed class GetPaymentStatsHandler(
    IPaymentStatsReader lecteur,
    ITimeCalendar calendrier,
    ICallerContext caller) : IQueryHandler<GetPaymentStatsQuery, PaymentStatsView>
{
    public async Task<PaymentStatsView> HandleAsync(
        GetPaymentStatsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        // VERIFIE ICI, PAS A LA PASSERELLE (ADR 0007). C'est la recette de
        // l'entreprise : un jeton de service, sans role, est refuse par la
        // meme ligne.
        if (!caller.Roles.Overlaps(HbaRoles.BackOffice))
        {
            throw new ForbiddenException("Les indicateurs de paiement sont réservés au back-office.");
        }

        var fenetre = calendrier.Validate(query.Window);
        var vue = await lecteur.ReadAsync(fenetre, query.Granularity, cancellationToken).ConfigureAwait(false);

        // Les periodes sans encaissement sont reintroduites ici : un GROUP BY
        // ne rend aucune ligne pour un jour sans paiement, et la courbe de
        // recette sauterait le creux au lieu de descendre a zero.
        var mesures = vue.CollectedSeries.ToDictionary(p => p.Key, p => p, StringComparer.Ordinal);

        var serie = calendrier
            .Keys(fenetre, query.Granularity)
            .Select(cle => mesures.TryGetValue(cle, out var point)
                ? point
                : new PaymentSeriesTally(cle, 0, 0))
            .ToList();

        return vue with { CollectedSeries = serie };
    }
}
