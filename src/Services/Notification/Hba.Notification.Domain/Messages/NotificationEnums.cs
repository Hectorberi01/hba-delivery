namespace Hba.Notification.Domain.Messages;

/// <summary>
/// Canaux d'envoi. Le SMS est le seul universel au Bénin ; WhatsApp suppose un
/// forfait data et une application installée, la notification poussée suppose
/// en plus l'application HBA. Aucun n'est interchangeable.
/// </summary>
public enum NotificationChannel
{
    Sms = 1,
    WhatsApp = 2,
    Push = 3,
}

public enum NotificationStatus
{
    /// <summary>Acceptée, pas encore remise au fournisseur.</summary>
    Pending = 1,

    /// <summary>Remise au fournisseur, qui l'a acceptée.</summary>
    Sent = 2,

    /// <summary>Refusée par le fournisseur, ou fournisseur injoignable.</summary>
    Failed = 3,

    /// <summary>
    /// Aucun fournisseur n'est configuré pour ce canal. Ce n'est pas un échec
    /// d'envoi : c'est une décision qui n'a pas encore été prise.
    /// </summary>
    Skipped = 4,
}
