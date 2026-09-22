using Hba.BuildingBlocks.Application.Abstractions;
using Hba.BuildingBlocks.Application.Messaging;
using Hba.BuildingBlocks.Domain;
using Hba.Notification.Application.Ports;
using Hba.Notification.Domain.Messages;
using Hba.Notification.Domain.Templates;
using Microsoft.Extensions.Logging;

namespace Hba.Notification.Application.Sending;

/// <summary>
/// Rend le modèle, envoie, consigne. Trois choses volontairement dans le même
/// geste : un envoi sans trace n'est pas instruisable, et une trace sans envoi
/// ne sert à rien.
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

        var template = TemplateCatalog.Get(command.TemplateId);

        if (template.Channel != command.Channel)
        {
            throw new DomainException(
                "CHANNEL_MISMATCH",
                $"Le modèle {template.Id} est prévu pour {template.Channel}, pas pour {command.Channel}.");
        }

        var now = clock.UtcNow;

        var trace = SentNotification.Create(
            command.Channel,
            command.Recipient,
            template.Id,
            command.CorrelationId,
            now);

        repository.Add(trace);

        // Le rendu peut échouer si l'appelant a oublié une variable. C'est son
        // bug, pas une panne : on le consigne et on n'appelle pas l'opérateur.
        string body;

        try
        {
            body = template.Render(command.Variables);
        }
        catch (DomainException ex)
        {
            trace.MarkFailed("aucun", ex.Message, now);
            await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            logger.LogError(
                "Rendu impossible pour {TemplateId} vers {Recipient} : {Message}",
                template.Id,
                Mask(command.Recipient),
                ex.Message);

            throw;
        }

        var sender = senders.FirstOrDefault(s => s.Channel == command.Channel);

        if (sender is null || !sender.IsConfigured)
        {
            trace.MarkSkipped($"Aucun fournisseur configuré pour {command.Channel}.", now);
            await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            logger.LogWarning(
                "Message {TemplateId} non envoyé vers {Recipient} : aucun fournisseur {Channel}.",
                template.Id,
                Mask(command.Recipient),
                command.Channel);

            return Unit.Value;
        }

        var result = await sender.SendAsync(command.Recipient, body, cancellationToken).ConfigureAwait(false);

        if (result.Succeeded)
        {
            trace.MarkSent(result.Provider, result.ProviderMessageId, clock.UtcNow);
        }
        else
        {
            trace.MarkFailed(result.Provider, result.Error ?? "échec sans détail", clock.UtcNow);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        if (!result.Succeeded)
        {
            // Relevé en exception pour que l'Inbox retienne l'échec et que le
            // message soit rejoué plutôt que perdu.
            throw new DomainException("NOTIFICATION_SEND_FAILED", result.Error ?? "Envoi refusé par le fournisseur.");
        }

        return Unit.Value;
    }

    /// <summary>
    /// Les journaux ne doivent pas devenir un annuaire téléphonique : on garde
    /// de quoi reconnaître un numéro sans le publier en clair.
    /// </summary>
    internal static string Mask(string recipient)
        => recipient.Length <= 4 ? "****" : string.Concat("****", recipient.AsSpan(recipient.Length - 4));
}
