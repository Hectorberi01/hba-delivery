using Hba.BuildingBlocks.Application.Messaging;
using Hba.Identity.Application.Features.Accounts.Commands;
using Microsoft.Extensions.Options;

namespace Hba.Identity.Api.Scheduling;

/// <summary>
/// Efface les comptes dont le délai de grâce est écoulé.
/// </summary>
///
/// <remarks>
/// LE SEUL ENDROIT DE TOUTE LA PLATEFORME QUI SUPPRIME UN COMPTE. Il ne décide
/// rien : la commande qu'il appelle porte la règle, et le domaine refuse
/// d'effacer un compte dont l'échéance n'est pas atteinte. Ce fichier-ci ne
/// fait que battre.
///
/// AUCUNE EXCEPTION NE SORT D'ICI. Un BackgroundService qui laisse échapper une
/// exception arrête l'hôte : une base injoignable trois secondes ferait tomber
/// Identity, donc toute l'authentification de la plateforme. Même leçon que sur
/// le planificateur de dispatch.
///
/// IL TOURNE DANS TOUS LES EXEMPLAIRES DU SERVICE, et c'est acceptable : deux
/// instances qui balaieraient en même temps liraient les mêmes lignes, mais la
/// seconde échouerait à l'écriture sur le jeton de concurrence (xmin) plutôt
/// que d'effacer deux fois. Le passage suivant reprendra ce qui a été manqué.
/// </remarks>
public sealed class PurgeDesComptes(
    IServiceScopeFactory scopeFactory,
    IOptions<AccountDeletionOptions> options,
    ILogger<PurgeDesComptes> journal) : BackgroundService
{
    private readonly AccountDeletionOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var periode = TimeSpan.FromMinutes(Math.Max(1, _options.SweepMinutes));

        journal.LogInformation(
            "Purge des comptes : delai de grace de {Jours} jours, balayage toutes les {Periode} minutes.",
            _options.GraceDays,
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
                journal.LogError(exception, "Echec d'un balayage de la purge des comptes.");
            }
        }
    }

    private async Task BalayerAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();

        var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();

        await dispatcher
            .SendAsync(new EraseDueAccountsCommand(), cancellationToken)
            .ConfigureAwait(false);
    }
}
