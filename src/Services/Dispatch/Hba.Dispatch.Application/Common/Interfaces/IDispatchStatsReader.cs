using Hba.BuildingBlocks.Application.Time;
using Hba.Dispatch.Domain.Dispatching;

namespace Hba.Dispatch.Application.Common.Interfaces;

public sealed record DispatchStatusTally(DispatchStatus Status, long Count);

public sealed record OfferStatusTally(OfferStatus Status, long Count);

public sealed record WaveTally(int WaveNumber, long Count);

public sealed record DurationTally(int AverageSeconds, long Samples);

/// <summary>
/// CE QUE MESURE CE LECTEUR : le moteur d'affectation, pas les livreurs.
///
/// Ni note, ni satisfaction, ni classement : rien de tout cela n'existe dans
/// le domaine, et l'inventer serait pire que de s'en passer. Ce que l'on sait
/// dire est plus utile de toute facon — combien de courses trouvent preneur,
/// en combien de temps, et a quelle vague.
/// </summary>
public sealed record DispatchStatsView(
    TimeWindow Window,
    IReadOnlyList<DispatchStatusTally> ByStatus,
    IReadOnlyList<OfferStatusTally> OffersByStatus,
    IReadOnlyList<WaveTally> AcceptedByWave,
    DurationTally ToAssignment,
    DurationTally ToOfferResponse);

public interface IDispatchStatsReader
{
    Task<DispatchStatsView> ReadAsync(TimeWindow window, CancellationToken cancellationToken);
}
