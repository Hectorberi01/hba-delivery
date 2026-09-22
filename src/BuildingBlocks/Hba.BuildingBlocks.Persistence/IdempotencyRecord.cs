using Hba.BuildingBlocks.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Hba.BuildingBlocks.Persistence;

/// <summary>
/// Clé d'idempotence déjà honorée, et ressource produite. Écrite dans la même
/// transaction que la ressource : un rejeu ne peut pas créer un doublon.
/// </summary>
public sealed class IdempotencyRecord
{
    public string Scope { get; set; } = string.Empty;

    public string Key { get; set; } = string.Empty;

    public string ResourceId { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class EfIdempotencyStore(DbContext context) : IIdempotencyStore
{
    public async Task<string?> TryGetResultAsync(
        string scope,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var record = await context.Set<IdempotencyRecord>()
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Scope == scope && r.Key == idempotencyKey, cancellationToken)
            .ConfigureAwait(false);

        return record?.ResourceId;
    }

    /// <summary>
    /// N'écrit pas tout de suite : la clé est validée avec la ressource, sinon
    /// un échec laisserait une clé sans ressource et bloquerait le rejeu.
    /// </summary>
    public Task RememberAsync(
        string scope,
        string idempotencyKey,
        string resourceId,
        CancellationToken cancellationToken)
    {
        context.Set<IdempotencyRecord>().Add(new IdempotencyRecord
        {
            Scope = scope,
            Key = idempotencyKey,
            ResourceId = resourceId,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        return Task.CompletedTask;
    }
}
