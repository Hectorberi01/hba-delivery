namespace Hba.Notification.Infrastructure.Senders;

public sealed class WhatsAppOptions
{
    public const string SectionName = "WhatsApp";

    /// <summary>« cloud » active l'API Cloud de Meta ; « none » n'envoie rien.</summary>
    public string Provider { get; set; } = "none";

    /// <summary>Identifiant du numéro expéditeur, donné par Meta.</summary>
    public string PhoneNumberId { get; set; } = string.Empty;

    /// <summary>
    /// Jeton d'accès permanent du compte système. IL NE VA PAS DANS LE DEPOT :
    /// variable d'environnement ou coffre, comme tout autre secret.
    /// </summary>
    public string AccessToken { get; set; } = string.Empty;

    /// <summary>
    /// Version de l'API Graph. Configurable et non figée dans le code : Meta
    /// retire chaque version environ deux ans après sa sortie, et une montée de
    /// version ne doit pas demander une livraison applicative.
    /// </summary>
    public string GraphApiVersion { get; set; } = "v24.0";

    public bool IsConfigured =>
        string.Equals(Provider, "cloud", StringComparison.OrdinalIgnoreCase)
        && !string.IsNullOrWhiteSpace(PhoneNumberId)
        && !string.IsNullOrWhiteSpace(AccessToken);
}
