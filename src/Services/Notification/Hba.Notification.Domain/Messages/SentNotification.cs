using Hba.BuildingBlocks.Domain;

namespace Hba.Notification.Domain.Messages;

/// <summary>
/// Trace d'un envoi. Elle sert à trois choses : répondre à « je n'ai pas reçu
/// mon code », rapprocher la facture de l'opérateur, et repérer un numéro qui
/// échoue systématiquement.
///
/// Le CONTENU RENDU n'est pas conservé : un code de connexion ou un code de
/// remise en base, lisible des mois plus tard, serait une base de secrets
/// périmés dont personne n'a besoin. On garde le modèle et le destinataire.
/// </summary>
public sealed class SentNotification : AggregateRoot
{
    private SentNotification()
    {
    }

    private SentNotification(
        Guid id,
        NotificationChannel channel,
        string recipient,
        string templateId,
        string? correlationId,
        DateTimeOffset createdAt) : base(id)
    {
        Channel = channel;
        Recipient = recipient;
        TemplateId = templateId;
        CorrelationId = correlationId;
        CreatedAt = createdAt;
        Status = NotificationStatus.Pending;
    }

    public NotificationChannel Channel { get; private set; }

    /// <summary>Téléphone pour SMS et WhatsApp, identifiant de compte pour le push.</summary>
    public string Recipient { get; private set; } = string.Empty;

    public string TemplateId { get; private set; } = string.Empty;

    public NotificationStatus Status { get; private set; }

    public string? Provider { get; private set; }

    /// <summary>Référence renvoyée par l'opérateur, pour le rapprochement.</summary>
    public string? ProviderMessageId { get; private set; }

    public string? Error { get; private set; }

    public int Attempts { get; private set; }

    public string? CorrelationId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public static SentNotification Create(
        NotificationChannel channel,
        string recipient,
        string templateId,
        string? correlationId,
        DateTimeOffset createdAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recipient);
        ArgumentException.ThrowIfNullOrWhiteSpace(templateId);

        return new SentNotification(Guid.CreateVersion7(), channel, recipient, templateId, correlationId, createdAt);
    }

    public void MarkSent(string provider, string? providerMessageId, DateTimeOffset now)
    {
        Attempts++;
        Status = NotificationStatus.Sent;
        Provider = provider;
        ProviderMessageId = providerMessageId;
        Error = null;
        CompletedAt = now;
    }

    public void MarkFailed(string provider, string error, DateTimeOffset now)
    {
        Attempts++;
        Status = NotificationStatus.Failed;
        Provider = provider;
        Error = error.Length > 500 ? error[..500] : error;
        CompletedAt = now;
    }

    /// <summary>
    /// Aucun fournisseur n'est branché sur ce canal. On le consigne comme tel
    /// plutôt que comme un échec : ce n'est pas la faute du réseau, c'est une
    /// décision qui reste à prendre.
    /// </summary>
    public void MarkSkipped(string reason, DateTimeOffset now)
    {
        Attempts++;
        Status = NotificationStatus.Skipped;
        Error = reason;
        CompletedAt = now;
    }
}
