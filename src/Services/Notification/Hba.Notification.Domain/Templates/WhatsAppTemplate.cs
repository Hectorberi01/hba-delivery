namespace Hba.Notification.Domain.Templates;

/// <summary>
/// Modèle WhatsApp d'authentification.
///
/// LE TEXTE N'EST PAS LE NOTRE. Meta impose le corps d'un modèle
/// d'authentification — « {{1}} is your verification code », traduit par Meta
/// selon le code de langue — et interdit les URL, les médias et les émojis. On
/// ne choisit ni la phrase, ni sa ponctuation : on choisit la langue, on
/// fournit le code, et Meta rend le reste.
///
/// Il n'y a donc rien à substituer ici, et aucune limite de 160 caractères :
/// ces deux préoccupations appartiennent au SMS.
///
/// Le code apparaît DEUX FOIS dans la charge utile — une fois dans le corps,
/// une fois dans le bouton de copie. C'est le format attendu, pas une erreur.
/// </summary>
public sealed class WhatsAppTemplate
{
    internal WhatsAppTemplate(string name, string language)
    {
        Name = name;
        Language = language;
    }

    /// <summary>Nom du modèle tel qu'il a été approuvé par Meta.</summary>
    public string Name { get; }

    /// <summary>Code de langue, « fr ». Il décide de la phrase affichée.</summary>
    public string Language { get; }
}
