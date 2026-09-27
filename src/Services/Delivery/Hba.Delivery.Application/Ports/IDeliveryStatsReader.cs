using Hba.BuildingBlocks.Application.Time;
using Hba.Delivery.Domain.Deliveries;

namespace Hba.Delivery.Application.Ports;

public sealed record StatusTally(DeliveryStatus Status, long Count, long BilledXof);

public sealed record SourceTally(DeliverySource Source, long Count);

public sealed record SeriesTally(string Key, long Count);

/// <summary>
/// Moyenne d'un délai, et le nombre de courses derrière.
///
/// LES DEUX VONT ENSEMBLE, TOUJOURS. Une moyenne sans son effectif se lit
/// comme une mesure : au lancement, « 18 minutes jusqu'à l'affectation »
/// pourra reposer sur trois trajets.
/// </summary>
public sealed record DurationTally(int AverageSeconds, long Samples);

public sealed record DeliveryStatsView(
    TimeWindow Window,
    IReadOnlyList<StatusTally> ByStatus,
    IReadOnlyList<SourceTally> BySource,
    IReadOnlyList<SeriesTally> CreatedSeries,
    DurationTally ToAssignment,
    DurationTally ToPickup,
    DurationTally ToDelivery);

/// <summary>
/// Lecture agrégée, hors de l'agrégat.
///
/// UNE PORTE SEPAREE DU DEPOT, ET C'EST VOULU. IDeliveryRepository charge des
/// agrégats pour les faire changer d'état ; ici on ne charge rien, on compte.
/// Passer par l'agrégat pour additionner cent mille courses reviendrait à les
/// matérialiser toutes en mémoire pour en tirer six nombres.
/// </summary>
public interface IDeliveryStatsReader
{
    Task<DeliveryStatsView> ReadAsync(
        TimeWindow window,
        TimeGranularity granularity,
        CancellationToken cancellationToken);
}
