using Hba.BuildingBlocks.Application.Time;
using Hba.Payment.Domain.Payments;

namespace Hba.Payment.Application.Common.Interfaces;

public sealed record PaymentStatusTally(PaymentStatus Status, long Count, long AmountXof);

public sealed record PaymentMethodTally(PaymentMethod Method, long Count, long AmountXof);

public sealed record PaymentSeriesTally(string Key, long Count, long AmountXof);

/// <summary>
/// DEUX LECTURES DE LA MEME FENETRE, ET C'EST LE POINT DELICAT DE CE SERVICE.
///
/// « Created » compte les intentions OUVERTES dans la fenetre : l'entonnoir.
/// « Collected » compte l'argent RECU dans la fenetre, quelle que soit la
/// date d'ouverture. Une intention ouverte le 31 aout et payee le
/// 1er septembre appartient a la cohorte d'aout et a la recette de septembre.
///
/// Les confondre donne une recette mensuelle qui ne tombe jamais juste, d'un
/// ecart trop petit pour etre remarque avant un rapprochement comptable.
/// </summary>
public sealed record PaymentStatsView(
    TimeWindow Window,
    IReadOnlyList<PaymentStatusTally> CreatedByStatus,
    IReadOnlyList<PaymentMethodTally> CreatedByMethod,
    long CollectedCount,
    long CollectedXof,
    IReadOnlyList<PaymentSeriesTally> CollectedSeries,
    int AverageSecondsToPayment,
    long PaymentSamples);

public interface IPaymentStatsReader
{
    Task<PaymentStatsView> ReadAsync(
        TimeWindow window,
        TimeGranularity granularity,
        CancellationToken cancellationToken);
}
