using Hba.BuildingBlocks.Application.Messaging;
using Hba.Dispatch.Application.Common.Interfaces;
using Hba.Dispatch.Application.Features.Dispatching.Commands;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Hba.Dispatch.Api.Scheduling;

/// <summary>
/// Le planificateur du referentiel : « expirations de devis, TIMEOUTS
/// D'OFFRE, rapprochement des paiements en attente ».
///
/// IL FAIT BATTRE LE MOTEUR. Toutes les vagues partent d'ici — la premiere
/// comme la derniere — et toutes les offres s'y eteignent. Un seul rythme,
/// un seul chemin.
/// </summary>
public sealed class DispatchScheduler(
    IServiceScopeFactory scopeFactory,
    IOptions<DispatchOptions> options,
    ILogger<DispatchScheduler> logger) : BackgroundService
{
    private const int BatchSize = 50;

    private readonly DispatchOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var periode = TimeSpan.FromSeconds(Math.Max(1, _options.SweepSeconds));

        logger.LogInformation(
            "Planificateur de dispatch : balayage toutes les {Periode}s, {Vagues} vagues a {Rayons} m, offres de {Delai}s.",
            periode.TotalSeconds,
            _options.WaveCount,
            string.Join(" / ", _options.WaveRadiiMeters),
            _options.OfferSeconds);

        using var timer = new PeriodicTimer(periode);

        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            // AUCUNE EXCEPTION NE SORT D'ICI. Un BackgroundService qui laisse
            // echapper une exception arrete l'hote : une base injoignable
            // trois secondes tuerait le moteur de dispatch pour de bon. La
            // lecon a deja ete payee sur le semeur de grilles tarifaires.
            try
            {
                await SweepAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Echec d'un balayage du planificateur de dispatch.");
            }
        }
    }

    private async Task SweepAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();

        var dispatches = scope.ServiceProvider.GetRequiredService<IDispatchRepository>();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();

        var ouvertes = await dispatches
            .FindDueAsync(DateTimeOffset.UtcNow, BatchSize, cancellationToken)
            .ConfigureAwait(false);

        foreach (var recherche in ouvertes)
        {
            // CHAQUE RECHERCHE DANS SA PROPRE PORTEE, donc sa propre
            // transaction. Sans cela, un conflit de concurrence sur une
            // course — un livreur qui accepte pendant le balayage, ce qui est
            // le cas courant — ferait echouer le traitement de toutes les
            // autres du lot.
            using var portee = scopeFactory.CreateScope();
            var unitaire = portee.ServiceProvider.GetRequiredService<IDispatcher>();

            try
            {
                await unitaire
                    .SendAsync(new AdvanceDispatchCommand(recherche.DeliveryId), cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                logger.LogWarning(
                    exception,
                    "La livraison {DeliveryId} n'a pas pu avancer ; reprise au prochain balayage.",
                    recherche.DeliveryId);
            }
        }
    }
}
