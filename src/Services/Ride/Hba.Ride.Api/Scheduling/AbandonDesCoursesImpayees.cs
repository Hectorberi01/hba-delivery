using Hba.BuildingBlocks.Application.Messaging;
using Hba.Ride.Application.Features.Rides.Commands.Internal;
using Microsoft.Extensions.Options;

namespace Hba.Ride.Api.Scheduling;

/// <summary>
/// Abandonne les courses de personnes qu'aucun paiement n'est venu confirmer.
/// </summary>
///
/// <remarks>
/// LA TRANSITION EXISTAIT DEPUIS LE PREMIER JOUR, ET PERSONNE NE LA DECLENCHAIT.
/// La table des transitions autorise <c>Requested → PaymentFailed</c> depuis
/// l'ecriture du service, et seul le webhook du fournisseur l'empruntait : un
/// passager qui fermait sa page de paiement laissait une course ouverte POUR
/// TOUJOURS. Delivery avait son balayage depuis le 29 septembre 2026 ; Ride, ne
/// deux jours plus tard, ne l'a jamais eu.
///
/// IL NE DECIDE RIEN. Le delai vit dans les reglages, la regle dans la commande,
/// et le refus dans le domaine. Ce fichier-ci ne fait que battre.
///
/// AUCUNE EXCEPTION NE SORT D'ICI. Un BackgroundService qui en laisse echapper
/// une arrete l'hote : une base injoignable trois secondes ferait tomber Ride,
/// donc toutes les courses de personnes de la plateforme.
///
/// IL TOURNE DANS TOUS LES EXEMPLAIRES DU SERVICE, et c'est acceptable : deux
/// instances qui balaieraient en meme temps liraient les memes lignes, et la
/// seconde echouerait a l'ecriture sur le jeton de concurrence plutot que
/// d'abandonner deux fois la meme course.
/// </remarks>
public sealed class AbandonDesCoursesImpayees(
    IServiceScopeFactory scopeFactory,
    IOptions<UnpaidRideOptions> options,
    ILogger<AbandonDesCoursesImpayees> journal) : BackgroundService
{
    private readonly UnpaidRideOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var periode = TimeSpan.FromMinutes(Math.Max(1, _options.SweepMinutes));

        journal.LogInformation(
            "Abandon des courses impayees : delai de {Delai} minutes, balayage toutes les {Periode} minutes.",
            _options.GraceMinutes,
            periode.TotalMinutes);

        using var timer = new PeriodicTimer(periode);

        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                await BalayerAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                journal.LogError(exception, "Echec d'un balayage des courses impayees.");
            }
        }
    }

    private async Task BalayerAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();

        var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();

        await dispatcher
            .SendAsync(new ExpireUnpaidRidesCommand(), cancellationToken)
            .ConfigureAwait(false);
    }
}
