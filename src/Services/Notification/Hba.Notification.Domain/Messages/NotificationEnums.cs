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

    /// <summary>
    /// Le courriel. FACULTATIF PAR NATURE DANS CE PRODUIT : on s'inscrit avec
    /// un téléphone, l'adresse se donne après, ou jamais. Un canal dont on sait
    /// d'avance qu'il manquera souvent — d'où « ignoré », qui n'est pas un
    /// échec.
    ///
    /// IL NE PORTERA JAMAIS DE CODE DE CONNEXION. Un courriel met parfois des
    /// minutes à arriver, se range dans les indésirables, et suppose un compte
    /// que le destinataire d'un colis n'a pas. Il sert à ce qui se garde — un
    /// reçu, un récapitulatif — pas à ce qui presse.
    /// </summary>
    Email = 4,
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
