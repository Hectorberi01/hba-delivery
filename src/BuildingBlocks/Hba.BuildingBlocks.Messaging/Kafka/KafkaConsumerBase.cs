using Confluent.Kafka;
using Google.Protobuf;
using Hba.BuildingBlocks.Messaging.Inbox;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Hba.BuildingBlocks.Messaging.Kafka;

/// <summary>
/// Consommateur Kafka avec Inbox. Le cycle est toujours le même : désérialiser,
/// tenter d'ouvrir l'Inbox, traiter, marquer, acquitter. L'offset n'est validé
/// qu'après traitement pour ne jamais perdre un message.
/// </summary>
/// <typeparam name="TMessage">Message protobuf du topic.</typeparam>
public abstract class KafkaConsumerBase<TMessage>(
    IServiceScopeFactory scopeFactory,
    IOptions<KafkaOptions> options,
    ILogger logger) : BackgroundService
    where TMessage : IMessage<TMessage>, new()
{
    private readonly KafkaOptions _options = options.Value;

    /// <summary>
    /// Tentatives sur place avant d'abandonner un message. Au-delà, le problème
    /// n'est pas passager et se règle par un correctif, pas par une relance.
    /// </summary>
    protected virtual int MaxDeliveryAttempts => 3;

    /// <summary>Topic écouté.</summary>
    protected abstract string Topic { get; }

    /// <summary>Identifiant de l'événement, lu dans l'enveloppe. Clé de l'Inbox.</summary>
    protected abstract Guid GetEventId(TMessage message);

    /// <summary>Nom qualifié du type, pour la journalisation et l'Inbox.</summary>
    protected abstract string GetEventType(TMessage message);

    /// <summary>Traitement métier. Doit être idempotent malgré l'Inbox.</summary>
    protected abstract Task HandleAsync(TMessage message, IServiceProvider services, CancellationToken cancellationToken);

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
        => Task.Run(() => ConsumeLoop(stoppingToken), stoppingToken);

    private async Task ConsumeLoop(CancellationToken stoppingToken)
    {
        var config = new ConsumerConfig
        {
            BootstrapServers = _options.BootstrapServers,
            GroupId = _options.ConsumerGroupId,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false,
            SecurityProtocol = Enum.Parse<SecurityProtocol>(_options.SecurityProtocol, ignoreCase: true),
        };

        using var consumer = new ConsumerBuilder<string, byte[]>(config).Build();
        consumer.Subscribe(Topic);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                ConsumeResult<string, byte[]>? result;
                try
                {
                    result = consumer.Consume(stoppingToken);
                }
                catch (ConsumeException ex)
                {
                    logger.LogError(ex, "Erreur de consommation sur {Topic}.", Topic);
                    continue;
                }

                if (result?.Message is null)
                {
                    continue;
                }

                // Un message qui échoue est rejoué sur place, avec un délai
                // croissant. Sans cela, l'exception remonterait jusqu'à la
                // boucle et arrêterait le consommateur : une seule notification
                // mal formée suffirait à faire taire tout le service.
                var handled = false;

                for (var attempt = 1; attempt <= MaxDeliveryAttempts && !handled; attempt++)
                {
                    try
                    {
                        await ProcessAsync(result, stoppingToken).ConfigureAwait(false);
                        handled = true;
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
#pragma warning disable CA1031 // L'échec est consigné dans l'Inbox, il ne doit pas tuer la boucle.
                    catch (Exception ex)
#pragma warning restore CA1031
                    {
                        logger.LogError(
                            ex,
                            "Traitement en échec sur {Topic} (offset {Offset}), tentative {Attempt}/{Max}.",
                            Topic,
                            result.Offset.Value,
                            attempt,
                            MaxDeliveryAttempts);

                        if (attempt < MaxDeliveryAttempts)
                        {
                            await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)), stoppingToken)
                                .ConfigureAwait(false);
                        }
                    }
                }

                if (!handled)
                {
                    // On avance quand même : bloquer la partition ferait tomber
                    // tout le trafic de ce topic pour un seul message. La trace
                    // reste dans inbox_messages, avec l'erreur et le compteur.
                    logger.LogError(
                        "Message abandonné sur {Topic} à l'offset {Offset} après {Max} tentatives. "
                        + "Voir inbox_messages pour le détail.",
                        Topic,
                        result.Offset.Value,
                        MaxDeliveryAttempts);
                }

                consumer.Commit(result);
            }
        }
        catch (OperationCanceledException)
        {
            // Arrêt normal.
        }
        finally
        {
            consumer.Close();
        }
    }

    private async Task ProcessAsync(ConsumeResult<string, byte[]> result, CancellationToken cancellationToken)
    {
        TMessage message;
        try
        {
            var parser = new MessageParser<TMessage>(() => new TMessage());
            message = parser.ParseFrom(result.Message.Value);
        }
        catch (InvalidProtocolBufferException ex)
        {
            // Message illisible : on ne bloque pas la partition. À router vers une
            // dead letter queue quand le besoin sera avéré.
            logger.LogError(ex, "Message illisible sur {Topic} à l'offset {Offset}.", Topic, result.Offset.Value);
            return;
        }

        var eventId = GetEventId(message);
        var eventType = GetEventType(message);

        using var scope = scopeFactory.CreateScope();
        var inbox = scope.ServiceProvider.GetRequiredService<IInboxStore>();

        if (!await inbox.TryBeginAsync(eventId, eventType, Topic, cancellationToken).ConfigureAwait(false))
        {
            logger.LogDebug("Événement {EventId} déjà traité, ignoré.", eventId);
            return;
        }

        try
        {
            await HandleAsync(message, scope.ServiceProvider, cancellationToken).ConfigureAwait(false);
            await inbox.MarkProcessedAsync(eventId, cancellationToken).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // L'échec est consigné dans l'Inbox, pas propagé à la boucle.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            logger.LogError(ex, "Échec du traitement de {EventType} ({EventId}).", eventType, eventId);
            await inbox.MarkFailedAsync(eventId, ex.Message, cancellationToken).ConfigureAwait(false);
            throw;
        }
    }
}
