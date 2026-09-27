namespace Hba.Notification.Infrastructure.Senders;

public sealed class OvhSmsOptions
{
    public const string SectionName = "Ovh";

    /// <summary>Point d'entrée régional. « eu » pour un compte OVHcloud Europe.</summary>
    public string Endpoint { get; set; } = "https://eu.api.ovh.com/1.0";

    /// <summary>Nom du service SMS, de la forme sms-xx123456-1.</summary>
    public string ServiceName { get; set; } = string.Empty;

    public string ApplicationKey { get; set; } = string.Empty;

    /// <summary>SECRET. Variable d'environnement ou coffre, jamais le dépôt.</summary>
    public string ApplicationSecret { get; set; } = string.Empty;

    /// <summary>SECRET. Jeton lié au compte, obtenu à la création des identifiants.</summary>
    public string ConsumerKey { get; set; } = string.Empty;

    /// <summary>
    /// Nom d'expéditeur déclaré chez OVHcloud. Vide fait basculer sur un numéro
    /// court permettant la réponse — pratique pour un test, à éviter pour un
    /// code de connexion, auquel personne n'a à répondre.
    /// </summary>
    public string Sender { get; set; } = string.Empty;

    /// <summary>
    /// SUJET JURIDIQUE, PAS TECHNIQUE. La mention « STOP » est obligatoire sur
    /// un SMS de prospection et inutile sur un message transactionnel — un code
    /// de connexion ou de remise en est un. La laisser coûterait une vingtaine
    /// de caractères sur les 160 disponibles, donc parfois un second SMS.
    /// Repasser à false si ce compte sert un jour à de la prospection.
    /// </summary>
    public bool NoStopClause { get; set; } = true;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ServiceName)
        && !string.IsNullOrWhiteSpace(ApplicationKey)
        && !string.IsNullOrWhiteSpace(ApplicationSecret)
        && !string.IsNullOrWhiteSpace(ConsumerKey);
}
