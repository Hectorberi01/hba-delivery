using Hba.BuildingBlocks.Messaging.Inbox;
using Microsoft.EntityFrameworkCore;

namespace Hba.BuildingBlocks.Persistence;

/// <summary>
/// Déduplication des messages entrants. La clé primaire de la table tranche les
/// courses entre instances : pas besoin de verrou applicatif.
/// </summary>
public sealed class EfInboxStore(DbContext context) : IInboxStore
{
    public async Task<bool> TryBeginAsync(
        Guid eventId,
        string eventType,
        string topic,
        CancellationToken cancellationToken)
    {
        var existing = await context.Set<InboxMessage>()
            .FirstOrDefaultAsync(m => m.EventId == eventId, cancellationToken)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            // Déjà vu. S'il n'a jamais abouti, on retente ; sinon on acquitte.
            return existing.ProcessedAt is null;
        }

        context.Set<InboxMessage>().Add(new InboxMessage
        {
            EventId = eventId,
            EventType = eventType,
            Topic = topic,
            ReceivedAt = DateTimeOffset.UtcNow,
            Attempts = 1,
        });

        try
        {
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (DbUpdateException)
        {
            // Une autre instance a inséré la même clé : elle s'en charge.
            return false;
        }
    }

    public async Task MarkProcessedAsync(Guid eventId, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;

        await context.Set<InboxMessage>()
            .Where(m => m.EventId == eventId)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.ProcessedAt, now), cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task MarkFailedAsync(Guid eventId, string error, CancellationToken cancellationToken)
    {
        var truncated = EfOutboxStore.Truncate(error);

        await context.Set<InboxMessage>()
            .Where(m => m.EventId == eventId)
            .ExecuteUpdateAsync(
                s => s
                    .SetProperty(m => m.LastError, truncated)
                    .SetProperty(m => m.Attempts, m => m.Attempts + 1),
                cancellationToken)
            .ConfigureAwait(false);
    }
}
