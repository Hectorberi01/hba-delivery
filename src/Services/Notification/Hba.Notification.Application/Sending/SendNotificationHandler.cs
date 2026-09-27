using Hba.BuildingBlocks.Application.Abstractions;
using Hba.BuildingBlocks.Application.Messaging;
using Hba.BuildingBlocks.Domain;
using Hba.Notification.Application.Ports;
using Hba.Notification.Domain.Messages;
using Hba.Notification.Domain.Templates;
using Microsoft.Extensions.Logging;

namespace Hba.Notification.Application.Sending;

/// <summary>
/// Choisit le canal, rend le message, envoie, consigne.
///
/// UNE TRACE PAR TENTATIVE. Quand WhatsApp échoue et que le SMS prend le
/// relais, deux lignes sont écrites : c'est ce qui permet, six mois plus tard,
/// de savoir combien de messages sont réellement passés par le repli — et donc
/// ce que le repli coûte.
/// </summary>
public sealed class SendNotificationHandler(
    IEnumerable<INotificationSender> senders,
    ISentNotificationRepository repository,
    IUnitOfWork unitOfWork,
    IClock clock,
    ILogger<SendNotificationHandler> logger) : ICommandHandler<SendNotificationCommand, Unit>
{
    public async Task<Unit> HandleAsync(SendNotificationCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var binding = TemplateCatalog.Get(command.TemplateId);
        var chain = ResolveChain(binding, command.Channel);

        if (chain.Count == 0)
        {
            throw new DomainException(
                "CHANNEL_UNSUPPORTED",
                $"Le message {binding.Id} n'est pas prévu pour {command.Channel}.");
        }

        SendResult? last = null;

        foreach (var channel in chain)
        {
            last = await AttemptAsync(binding, channel, command, cancellationToken).ConfigureAwait(false);

            if (last.Succeeded)
            {
                return Unit.Value;
            }
        }

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // Relevé en exception pour que l'Inbox retienne l'échec et que le
        // message soit rejoué plutôt que perdu.
        throw new DomainException(
            "NOTIFICATION_SEND_FAILED",
            last?.Error ?? "Aucun canal n'a pu remettre le message.");
    }

    /// <summary>
    /// Canaux à essayer. Un appelant qui n'impose rien laisse le catalogue
    /// décider ; un appelant qui impose un canal n'obtient que celui-là, sans
    /// repli — c'est ce qui permet de forcer le SMS pour un destinataire.
    /// </summary>
    private static IReadOnlyList<NotificationChannel> ResolveChain(
        TemplateBinding binding,
        NotificationChannel? requested)
    {
        if (requested is null)
        {
            return binding.Channels.Where(binding.Supports).ToList();
        }

        return binding.Supports(requested.Value) ? [requested.Value] : [];
    }

    private async Task<SendResult> AttemptAsync(
        TemplateBinding binding,
        NotificationChannel channel,
        SendNotificationCommand command,
        CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;

        var trace = SentNotification.Create(
            channel,
            command.Recipient,
            binding.Id,
            command.CorrelationId,
            now);

        repository.Add(trace);

        OutboundMessage message;

        try
        {
            message = Build(binding, channel, command.Variables);
        }
        catch (DomainException ex)
        {
            // Variable manquante : c'est le bug de l'appelant, pas une panne.
            // On le consigne et on n'appelle aucun fournisseur.
            trace.MarkFailed("aucun", ex.Message, now);
            await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            logger.LogError(
                "Rendu impossible pour {TemplateId} en {Channel} vers {Recipient} : {Message}",
                binding.Id,
                channel,
                Mask(command.Recipient),
                ex.Message);

            throw;
        }

        var sender = senders.FirstOrDefault(s => s.Channel == channel);

        if (sender is null || !sender.IsConfigured)
        {
            trace.MarkSkipped($"Aucun fournisseur configuré pour {channel}.", now);
            await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            logger.LogWarning(
                "Message {TemplateId} non envoyé en {Channel} vers {Recipient} : aucun fournisseur.",
                binding.Id,
                channel,
                Mask(command.Recipient));

            return SendResult.Failed("aucun", $"Aucun fournisseur configuré pour {channel}.");
        }

        var result = await sender
            .SendAsync(command.Recipient, message, cancellationToken)
            .ConfigureAwait(false);

        if (result.Succeeded)
        {
            trace.MarkSent(result.Provider, result.ProviderMessageId, clock.UtcNow);
            await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return result;
        }

        trace.MarkFailed(result.Provider, result.Error ?? "échec sans détail", clock.UtcNow);

        logger.LogWarning(
            "Echec {Channel} pour {TemplateId} vers {Recipient} : {Error}",
            channel,
            binding.Id,
            Mask(command.Recipient),
            result.Error);

        return result;
    }

    private static OutboundMessage Build(
        TemplateBinding binding,
        NotificationChannel channel,
        IReadOnlyDictionary<string, string> variables)
    {
        switch (channel)
        {
            case NotificationChannel.WhatsApp:
                var template = binding.RequireWhatsApp();

                if (!variables.TryGetValue("code", out var code) || string.IsNullOrWhiteSpace(code))
                {
                    throw new DomainException(
                        "MISSING_TEMPLATE_VARIABLE",
                        $"Le modèle WhatsApp {binding.Id} attend : code.");
                }

                return new WhatsAppAuthenticationMessage(template.Name, template.Language, code);

            case NotificationChannel.Sms:
                return new TextMessage(binding.RequireSms().Render(variables));

            default:
                throw new DomainException(
                    "CHANNEL_UNSUPPORTED",
                    $"Canal {channel} non pris en charge.");
        }
    }

    /// <summary>
    /// Les journaux ne doivent pas devenir un annuaire téléphonique : on garde
    /// de quoi reconnaître un numéro sans le publier en clair.
    /// </summary>
    internal static string Mask(string recipient)
        => recipient.Length <= 4 ? "****" : string.Concat("****", recipient.AsSpan(recipient.Length - 4));
}
