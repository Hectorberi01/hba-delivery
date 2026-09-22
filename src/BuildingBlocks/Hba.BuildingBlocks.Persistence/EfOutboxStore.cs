using Hba.BuildingBlocks.Messaging.Outbox;
using Microsoft.EntityFrameworkCore;

namespace Hba.BuildingBlocks.Persistence;

/// <summary>
/// Lecture et marquage de l'Outbox. FOR UPDATE SKIP LOCKED : plusieurs
/// instances du même service peuvent vider la file en parallèle sans se
/// disputer les mêmes lignes ni publier deux fois.
/// </summary>
public sealed class EfOutboxStore(DbContext context, EfMessagingOptions options) : IOutboxStore
{
    public async Task<IReadOnlyList<OutboxMessage>> DequeueBatchAsync(int batchSize, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);

        var now = DateTimeOffset.UtcNow;

        // Chaîne non interpolée : {0} et {1} sont les emplacements de paramètres
        // d'EF, et le nom de table est substitué séparément. Il vient de la
        // configuration du service, jamais d'une entrée utilisateur.
        const string template = """
            SELECT * FROM __TABLE__
            WHERE "PublishedAt" IS NULL
              AND ("NextAttemptAt" IS NULL OR "NextAttemptAt" <= {0})
            ORDER BY "OccurredAt"
            LIMIT {1}
            FOR UPDATE SKIP LOCKED
            """;

        var sql = template.Replace("__TABLE__", options.QualifiedOutboxTable, StringComparison.Ordinal);

        return await context.Set<OutboxMessage>()
            .FromSqlRaw(sql, now, batchSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task MarkPublishedAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ids);

        var now = DateTimeOffset.UtcNow;

        await context.Set<OutboxMessage>()
            .Where(m => ids.Contains(m.Id))
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.PublishedAt, now), cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task MarkFailedAsync(
        Guid id,
        string error,
        DateTimeOffset nextAttemptAt,
        CancellationToken cancellationToken)
    {
        var truncated = Truncate(error);

        await context.Set<OutboxMessage>()
            .Where(m => m.Id == id)
            .ExecuteUpdateAsync(
                s => s
                    .SetProperty(m => m.Attempts, m => m.Attempts + 1)
                    .SetProperty(m => m.LastError, truncated)
                    .SetProperty(m => m.NextAttemptAt, nextAttemptAt),
                cancellationToken)
            .ConfigureAwait(false);
    }

    internal static string Truncate(string? value)
        => value is null ? string.Empty : value.Length > 1000 ? value[..1000] : value;
}
