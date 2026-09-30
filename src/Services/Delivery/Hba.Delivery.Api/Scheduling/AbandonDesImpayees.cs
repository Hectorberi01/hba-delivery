using Hba.BuildingBlocks.Application.Messaging;
using Hba.Delivery.Application.Commands.Internal;
using Microsoft.Extensions.Options;

namespace Hba.Delivery.Api.Scheduling;

/// <summary>
/// Abandonne les courses qu'aucun paiement n'est venu confirmer.
/// </summary>
///
/// <remarks>
/// LA TRANSITION EXISTAIT DEPUIS LE DEBUT, ET PERSONNE NE LA DECLENCHAIT.
/// <c>DeliveryTransitions</c> autorise le planificateur à faire passer une
/// course de PENDING_PAYMENT à PAYMENT_FAILED depuis le premier jour ; il
/// n'existait simplement aucun planificateur. Une course dont le client
/// abandonnait le paiement restait « en cours » pour toujours.
///
/// IL NE DECIDE RIEN. Le délai vit dans les réglages, la règle dans la commande,
/// et le refus dans le domaine. Ce fichier-ci ne fait que battre.
///
/// AUCUNE EXCEPTION NE SORT D'ICI. Un BackgroundService qui en laisse échapper
/// une arrête l'hôte : une base injoignable trois secondes ferait tomber
/// Delivery, donc toutes les courses de la plateforme. Même leçon que sur la
/// purge des comptes et le planificateur de dispatch.
///
/// IL TOURNE DANS TOUS LES EXEMPLAIRES DU SERVICE, et c'est acceptable : deux
/// instances qui balaieraient en même temps liraient les mêmes lignes, et la
/// seconde échouerait à l'écriture sur le jeton de concurrence plutôt que
/// d'abandonner deux fois la même course.
/// </remarks>
public sealed class AbandonDesImpayees(
    IServiceScopeFactory scopeFactory,
    IOptions<UnpaidDeliveryOptions> options,
    ILogger<AbandonDesImpayees> journal) : BackgroundService
{
    private readonly UnpaidDeliveryOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var periode = TimeSpan.FromMinutes(Math.Max(1, _options.SweepMinutes));

        journal.LogInformation(
            "Abandon des impayees : delai de {Delai} minutes, balayage toutes les {Periode} minutes.",
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
            .SendAsync(new ExpireUnpaidDeliveriesCommand(), cancellationToken)
            .ConfigureAwait(false);
    }
}
