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
public sealed record WhatsAppAuthenticationMessage(
    string TemplateName,
    string Language,
    string Code) : OutboundMessage;
