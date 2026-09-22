using Hba.BuildingBlocks.Messaging.Kafka;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Hba.BuildingBlocks.Messaging.Outbox;

public sealed class OutboxOptions
{
    public const string SectionName = "Outbox";

    public int BatchSize { get; set; } = 100;

    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromSeconds(1);

    public int MaxAttempts { get; set; } = 10;

    public TimeSpan BaseBackoff { get; set; } = TimeSpan.FromSeconds(2);
}

/// <summary>
/// Vide l'Outbox vers Kafka. La livraison est « au moins une fois » : côté
/// consommateur, c'est l'Inbox qui assure l'idempotence.
/// </summary>
public sealed class OutboxDispatcher(
    IServiceScopeFactory scopeFactory,
    IKafkaProducer producer,
    IOptions<OutboxOptions> options,
    ILogger<OutboxDispatcher> logger) : BackgroundService
{
    private readonly OutboxOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var published = await PublishBatchAsync(stoppingToken).ConfigureAwait(false);
                if (published == 0)
                {
                    await Task.Delay(_options.PollingInterval, stoppingToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
#pragma warning disable CA1031 // Le dispatcher ne doit jamais s'arrêter sur une erreur ponctuelle.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                logger.LogError(ex, "Échec du cycle de publication de l'Outbox.");
                await Task.Delay(_options.PollingInterval, stoppingToken).ConfigureAwait(false);
            }
        }
    }

    private async Task<int> PublishBatchAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IOutboxStore>();

        var batch = await store.DequeueBatchAsync(_options.BatchSize, cancellationToken).ConfigureAwait(false);
        if (batch.Count == 0)
        {
            return 0;
        }

        var succeeded = new List<Guid>(batch.Count);

        foreach (var message in batch)
        {
            try
            {
                await producer.ProduceAsync(message, cancellationToken).ConfigureAwait(false);
                succeeded.Add(message.Id);
            }
#pragma warning disable CA1031 // Un message en échec ne doit pas bloquer les suivants.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                var delay = TimeSpan.FromSeconds(
                    _options.BaseBackoff.TotalSeconds * Math.Pow(2, Math.Min(message.Attempts, 8)));

                logger.LogWarning(
                    ex,
                    "Publication impossible pour {EventType} ({MessageId}), tentative {Attempt}.",
                    message.EventType,
                    message.Id,
                    message.Attempts + 1);

                await store
                    .MarkFailedAsync(message.Id, ex.Message, DateTimeOffset.UtcNow.Add(delay), cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        if (succeeded.Count > 0)
        {
            await store.MarkPublishedAsync(succeeded, cancellationToken).ConfigureAwait(false);
        }

        return batch.Count;
    }
}
