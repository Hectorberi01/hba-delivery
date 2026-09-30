using Hba.BuildingBlocks.Application.Messaging;
using Hba.Notification.Domain.Messages;

namespace Hba.Notification.Application.Sending;

/// <summary>
/// Un envoi demandé par un autre service. Les variables sont celles du modèle ;
/// Notification ne les interprète pas, il les substitue.
/// </summary>
public sealed record SendNotificationCommand(
    /// <summary>
    /// Canal imposé par l'appelant, ou nul pour laisser le catalogue choisir
    /// et basculer sur son repli. Imposer un canal, c'est aussi renoncer au
    /// repli : c'est voulu pour les cas où un seul canal est acceptable.
    /// </summary>
    NotificationChannel? Channel,
    string Recipient,
    string TemplateId,
    IReadOnlyDictionary<string, string> Variables,
    string? CorrelationId,
    /// <summary>
    /// Compte dont il faut RESOUDRE l'adresse, quand l'appelant ne la connaît
    /// pas. Renseigné, il remplace Recipient.
    /// </summary>
    ///
    /// <remarks>
    /// LES DEUX CHAMPS COEXISTENT PARCE QUE LES DEUX CAS EXISTENT. Identity
    /// connaît le téléphone à qui il envoie un code : il le met dans
    /// Recipient. Delivery sait qu'une course est terminée mais pas où envoyer
    /// le reçu : il met le compte ici. Un seul champ aurait obligé l'un des
    /// deux à mentir sur ce qu'il détient.
    /// </remarks>
    Guid? SubjectId = null,
    /// <summary>
    /// Identifiant du message d'origine, qui sert de clé d'idempotence chez le
    /// fournisseur.
    /// </summary>
    ///
    /// <remarks>
    /// L'INBOX NE SUFFIT PAS. Elle écarte bien un message rejoué par Kafka,
    /// mais pas le cas où l'envoi a RÉUSSI et où la validation de l'Inbox a
    /// échoué juste après : le message repart alors, légitimement, et le client
    /// reçoit deux fois son reçu. La clé referme cette fenêtre-là, côté
    /// fournisseur.
    ///
    /// IL FAUT QU'IL SOIT STABLE. C'est l'identifiant de l'événement qui a
    /// produit la commande — dérivé de façon déterministe par Delivery — et non
    /// une valeur tirée à l'envoi, qui changerait à chaque tentative et ne
    /// dédoublonnerait rien.
    /// </remarks>
    string? MessageId = null) : ICommand;
