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
    ICarnetDAdresses carnet,
    IUnitOfWork unitOfWork,
    IClock clock,
    ILogger<SendNotificationHandler> logger) : ICommandHandler<SendNotificationCommand, Unit>
{
    public async Task<Unit> HandleAsync(SendNotificationCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var binding = TemplateCatalog.Get(command.TemplateId);
        var chain = ResolveChain(binding, command.Channel);

        // L'ADRESSE SE RESOUT ICI, ET UNE SEULE FOIS. La commande porte un
        // identifiant de compte quand l'appelant ne connait pas l'adresse — un
        // recu de course, par exemple, dont Delivery sait tout sauf ou
        // l'envoyer. Resoudre dans AttemptAsync appellerait Directory une fois
        // par canal essaye.
        var destinataire = command.Recipient;

        if (command.SubjectId is { } sujet)
        {
            var adresse = await carnet.CourrielAsync(sujet, cancellationToken).ConfigureAwait(false);

            destinataire = adresse.Courriel ?? string.Empty;

            if (string.IsNullOrWhiteSpace(destinataire))
            {
                // PAS UNE EXCEPTION, ET SURTOUT PAS UN RENVOI. Un client sans
                // courriel est le cas COURANT : l'adresse est facultative dans
                // ce produit. Lever ferait rejouer ce message par l'Inbox
                // jusqu'a la fin des temps pour une raison qui ne changera pas
                // toute seule.
                //
                // LE MOTIF DIT LEQUEL DES DEUX, ET C'EST TOUT CE QUI A CHANGE
                // ICI. Il disait « le compte n'a pas de courriel » dans les deux
                // cas — y compris quand l'annuaire etait injoignable, ce qui est
                // un incident et non une absence d'adresse. Le journal accusait
                // le client d'une panne de Directory, et la vraie cause etait
                // devenue invisible.
                var motif = adresse.Indisponible
                    ? "L'annuaire n'a pas pu etre interroge : adresse inconnue."
                    : "Le compte n'a pas de courriel.";

                Consigner(binding, chain, sujet, command, motif);
                await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

                if (adresse.Indisponible)
                {
                    // EN AVERTISSEMENT, PARCE QUE C'EST UNE PANNE. Un client
                    // sans adresse est une information ; un annuaire muet est
                    // quelque chose a aller regarder.
                    logger.LogWarning(
                        "Message {TemplateId} non envoye au compte {Sujet} : l'annuaire n'a pas repondu.",
                        binding.Id,
                        sujet);
                }
                else
                {
                    logger.LogInformation(
                        "Message {TemplateId} non envoye : le compte {Sujet} n'a pas de courriel.",
                        binding.Id,
                        sujet);
                }

                return Unit.Value;
            }

            command = command with { Recipient = destinataire };
        }

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
            message = Build(binding, channel, command);
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
        SendNotificationCommand command)
    {
        var variables = command.Variables;

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

            case NotificationChannel.Email:
                var courriel = binding.RequireEmail().Render(variables);
                return new EmailMessage(courriel.Subject, courriel.Body, command.MessageId);

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
    {
        // UNE ADRESSE NE SE MASQUE PAS COMME UN NUMERO. Garder les quatre
        // derniers caracteres d'un telephone laisse de quoi le reconnaitre
        // sans le publier ; les garder d'une adresse ne laisse que « .com »,
        // qui n'identifie rien, et masque le domaine, qui aiderait justement a
        // diagnostiquer un rejet. On garde donc le domaine et la premiere
        // lettre : « h***@hbatechettrade.com ».
        var arobase = recipient.IndexOf('@', StringComparison.Ordinal);

        if (arobase > 0)
        {
            return string.Concat(recipient.AsSpan(0, 1), "***", recipient.AsSpan(arobase));
        }

        return recipient.Length <= 4
            ? "****"
            : string.Concat("****", recipient.AsSpan(recipient.Length - 4));
    }

    /// <summary>
    /// Ecrit une trace « ignore » sans appeler le moindre fournisseur.
    /// </summary>
    ///
    /// <remarks>
    /// ELLE EXISTE PARCE QUE L'ABSENCE D'ADRESSE SE CONSTATE AVANT LE CANAL.
    /// Le chemin normal consigne dans AttemptAsync, une ligne par tentative ;
    /// ici il n'y a rien a tenter, et ne rien ecrire du tout laisserait croire
    /// que le recu est parti.
    /// </remarks>
    private void Consigner(
        TemplateBinding binding,
        IReadOnlyList<NotificationChannel> chain,
        Guid sujet,
        SendNotificationCommand command,
        string motif)
    {
        var canal = chain.Count > 0 ? chain[0] : NotificationChannel.Email;

        var trace = SentNotification.Create(
            canal,
            sujet.ToString(),
            binding.Id,
            command.CorrelationId,
            clock.UtcNow);

        repository.Add(trace);
        trace.MarkSkipped(motif, clock.UtcNow);
    }
}
