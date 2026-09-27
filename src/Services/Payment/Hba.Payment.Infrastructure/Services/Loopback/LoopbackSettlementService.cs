using Hba.BuildingBlocks.Application.Messaging;
using Hba.Payment.Application.Features.Payments.Commands;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Hba.Payment.Infrastructure.Services.Loopback;

/// <summary>
/// Tient le role du payeur : quelques secondes apres l'ouverture, il denoue la
/// transaction en passant par le meme chemin qu'une notification recue.
/// </summary>
internal sealed class LoopbackSettlementService(
    LoopbackPaymentProvider provider,
    IServiceScopeFactory scopes,
    ILogger<LoopbackSettlementService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var reference in provider.ADenouer.ReadAllAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                await Task.Delay(provider.Delai, stoppingToken).ConfigureAwait(false);

                using var scope = scopes.CreateScope();
                var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();

                var outcome = await dispatcher
                    .SendAsync(new ApplyProviderOutcomeCommand(reference), stoppingToken)
                    .ConfigureAwait(false);

                logger.LogWarning("Paiement factice {Reference} denoue : {Outcome}.", reference, outcome);
            }
            catch (OperationCanceledException)
            {
                // Arret normal.
                return;
            }
#pragma warning disable CA1031 // Une transaction factice qui echoue ne doit pas tuer l'hote.
            catch (Exception exception)
#pragma warning restore CA1031
            {
                logger.LogError(exception, "Denouement du paiement factice {Reference} en echec.", reference);
            }
        }
    }
}
