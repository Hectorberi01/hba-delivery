using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Hba.Payment.Infrastructure.Services.FedaPay;

/// <summary>
/// Annonce au demarrage vers quel compte FedaPay le service encaisse.
///
/// CE N'EST PAS DU CONFORT. Un service de paiement qui demarre « en bonne
/// sante » avec des cles de bac a sable en production ne lever aucune alerte :
/// il accepte les commandes, ouvre des transactions, et personne n'est jamais
/// debite. La panne se decouvre a la comptabilite, des semaines plus tard. Une
/// ligne au demarrage coute une ligne de journal et supprime cette classe
/// entiere de surprises.
/// </summary>
internal sealed class FedaPayStartupReport(
    IOptions<FedaPayOptions> options,
    IHostEnvironment environment,
    ILogger<FedaPayStartupReport> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;

        if (!settings.IsConfigured)
        {
            logger.LogError(
                "PAIEMENT NON CONFIGURE : aucune cle FedaPay. Aucune course ne pourra etre payee.");

            return Task.CompletedTask;
        }

        if (string.IsNullOrWhiteSpace(settings.WebhookSecret))
        {
            logger.LogError(
                "SECRET DE WEBHOOK ABSENT : les notifications de FedaPay seront toutes refusees, "
                + "donc aucun paiement ne sera jamais confirme.");
        }

        if (settings.IsLive && environment.IsDevelopment())
        {
            logger.LogWarning(
                "CLES FEDAPAY DE PRODUCTION SUR UN POSTE DE DEVELOPPEMENT : les paiements de test "
                + "debiteront de vrais comptes.");
        }

        if (!settings.IsLive && !environment.IsDevelopment())
        {
            logger.LogWarning(
                "COMPTE FEDAPAY DE BAC A SABLE HORS DEVELOPPEMENT ({Environment}) : "
                + "les paiements aboutiront sans qu'aucun argent ne change de main.",
                environment.EnvironmentName);
        }

        logger.LogInformation(
            "Paiements via {Provider}, API {BaseAddress}, tolerance webhook {Tolerance}s.",
            settings.IsLive ? "FedaPay LIVE" : "FedaPay bac a sable",
            settings.BaseAddress,
            settings.WebhookToleranceSeconds);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
