namespace Hba.Notification.Application.Ports;

/// <summary>
/// Ce qui part réellement chez un fournisseur.
///
/// Un SMS est un texte. Un message WhatsApp d'authentification est le NOM d'un
/// modèle approuvé par Meta, une langue et un code — le texte appartient à
/// Meta. Les deux ne se ramènent pas l'un à l'autre, et prétendre le contraire
/// obligerait un adaptateur à deviner ce qu'on lui a donné.
/// </summary>
public abstract record OutboundMessage;

/// <summary>Texte rendu, prêt à partir. SMS, et plus tard courriel.</summary>
public sealed record TextMessage(string Body) : OutboundMessage;

/// <summary>
/// Modèle WhatsApp d'authentification. Le code voyage à part parce que Meta
/// l'attend à deux endroits de la charge utile : le corps et le bouton de
/// copie.
/// </summary>
/// <summary>
/// Un courriel : un objet ET un corps.
/// </summary>
///
/// <remarks>
/// IL NE SE RAMENE PAS A TextMessage, ET C'EST TOUT LE PROPOS DE CE FICHIER.
/// Un SMS n'a pas d'objet ; le forcer dans un TextMessage obligerait
/// l'adaptateur à découper une convention — première ligne = objet — que rien
/// n'imposerait, et qu'un modèle finirait par oublier.
///
/// LA CLE D'IDEMPOTENCE EST SUR LE MESSAGE, PAS SUR LE PORT. Seul le courriel
/// en a un usage : Resend l'honore pendant vingt-quatre heures, un relais SMTP
/// ne saurait qu'en faire. La poser sur INotificationSender aurait obligé les
/// adaptateurs SMS et WhatsApp à porter un paramètre qu'ils ignorent.
///
/// ELLE EST NULLE QUAND ON NE PEUT PAS LA CONSTRUIRE, et l'envoi part quand
/// même : un doublon vaut mieux qu'un reçu qui ne part pas.
/// </remarks>
public sealed record EmailMessage(string Subject, string Body, string? IdempotencyKey = null)
    : OutboundMessage;

public sealed record WhatsAppAuthenticationMessage(
    string TemplateName,
    string Language,
    string Code) : OutboundMessage;
