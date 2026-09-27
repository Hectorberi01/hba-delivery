using Hba.Notification.Application.Ports;
using Hba.Notification.Domain.Messages;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Hba.Notification.Infrastructure.Senders;

/// <summary>
/// Annonce au demarrage quels canaux ont reellement un fournisseur.
///
/// SANS CETTE LIGNE, UN SERVICE SANS OPERATEUR DEMARRE EXACTEMENT COMME UN
/// SERVICE OPERATIONNEL. La difference n'apparait qu'au premier message, sous
/// la forme d'un « Aucun fournisseur configure » repete trois fois, a l'heure
/// ou quelqu'un attend son code — et il faut alors remonter toute la chaine,
/// de l'Outbox au groupe de consommateurs, pour decouvrir que la configuration
/// n'a jamais ete lue.
///
/// Resoudre les adaptateurs ici a un second effet voulu : cela declenche les
/// journaux poses sur leur construction, donc la valeur exacte lue dans
/// Sms:Provider, au demarrage plutot qu'au premier envoi.
/// </summary>
internal sealed class SenderStartupReport(
    IEnumerable<INotificationSender> senders,
    ILogger<SenderStartupReport> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        // GroupBy et non ToDictionary : deux adaptateurs enregistres pour le
        // meme canal feraient lever ToDictionary, et un rapport de demarrage ne
        // doit jamais empecher le service de demarrer.
        var byChannel = senders
            .GroupBy(sender => sender.Channel)
            .ToDictionary(group => group.Key, group => group.ToList());

        foreach (var channel in Enum.GetValues<NotificationChannel>())
        {
            if (!byChannel.TryGetValue(channel, out var registered))
            {
                logger.LogWarning("Canal {Canal} : aucun adaptateur enregistre.", channel);
                continue;
            }

            if (registered.Count > 1)
            {
                logger.LogWarning(
                    "Canal {Canal} : {Nombre} adaptateurs enregistres ({Adaptateurs}). "
                    + "Le premier l'emporte, ce qui rend l'envoi dependant de l'ordre d'enregistrement.",
                    channel,
                    registered.Count,
                    string.Join(", ", registered.Select(s => s.GetType().Name)));
            }

            var sender = registered[0];

            if (sender.IsConfigured)
            {
                logger.LogInformation(
                    "Canal {Canal} : {Adaptateur}, configure.",
                    channel,
                    sender.GetType().Name);
            }
            else
            {
                // AVERTISSEMENT ET NON INFORMATION : un canal sans fournisseur
                // n'est pas un choix neutre. Le SMS porte le code de remise,
                // qui est la preuve de livraison ; sans lui aucune livraison
                // ne peut etre cloturee.
                logger.LogWarning(
                    "Canal {Canal} : AUCUN FOURNISSEUR. Les messages de ce canal ne partiront pas.",
                    channel);
            }
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
