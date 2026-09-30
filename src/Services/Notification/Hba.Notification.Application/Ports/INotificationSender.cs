using Hba.Notification.Domain.Messages;

namespace Hba.Notification.Application.Ports;

public sealed record SendResult(bool Succeeded, string Provider, string? ProviderMessageId, string? Error)
{
    public static SendResult Ok(string provider, string? messageId = null) => new(true, provider, messageId, null);

    public static SendResult Failed(string provider, string error) => new(false, provider, null, error);
}

/// <summary>
/// Un canal, un adaptateur. Le domaine ne connaît ni opérateur, ni format
/// d'API : il sait seulement qu'un message part, ou ne part pas.
/// </summary>
public interface INotificationSender
{
    NotificationChannel Channel { get; }

    /// <summary>
    /// Vrai si un fournisseur est réellement configuré. Faux fait consigner
    /// l'envoi en « ignoré » au lieu d'« échoué » : la nuance compte quand on
    /// relit le journal six mois plus tard.
    /// </summary>
    bool IsConfigured { get; }

    Task<SendResult> SendAsync(
        string recipient,
        OutboundMessage message,
        CancellationToken cancellationToken);
}

/// <summary>
/// Où Notification va chercher l'adresse d'un compte.
/// </summary>
///
/// <remarks>
/// POURQUOI NOTIFICATION RESOUT LUI-MEME, ALORS QUE POUR LE SMS C'EST
/// L'APPELANT. Identity connaît le téléphone à qui il envoie un code : il le
/// met dans la commande. Mais le service qui sait qu'une course est terminée,
/// c'est Delivery, et l'adresse vit dans Directory. Faire appeler Directory par
/// Delivery mettrait un appel réseau dans la transaction qui clôt une
/// livraison — Directory injoignable, et le livreur ne peut plus clore sa
/// mission. Ici, un échec ne coûte qu'un renvoi du message.
///
/// « PAS D'ADRESSE » ET « JE N'AI PAS PU DEMANDER » NE SONT PAS LA MEME CHOSE,
/// et les confondre a coûté tous les reçus. Le courriel est facultatif dans ce
/// produit : un client sans adresse est le cas courant, et l'envoi est
/// simplement consigné « ignoré ». Une panne de Directory, elle, est un
/// incident — et pendant des semaines elle s'est lue « le compte n'a pas de
/// courriel », motif faux qui a rendu la vraie cause invisible.
///
/// D'OU <see cref="Adresse"/>, qui porte les deux cas separement. Aucune des
/// deux ne lève : lever ferait rejouer le message par l'Inbox, ce qui est utile
/// pour une panne passagère et désastreux pour un client qui n'a pas d'adresse —
/// et l'un ne se distingue pas de l'autre depuis l'appelant.
/// </remarks>
public interface ICarnetDAdresses
{
    Task<Adresse> CourrielAsync(Guid subjectId, CancellationToken cancellationToken);
}

/// <summary>
/// Ce que l'annuaire a répondu.
/// </summary>
///
/// <param name="Courriel">L'adresse, ou null s'il n'y en a pas.</param>
/// <param name="Indisponible">
/// Vrai quand l'annuaire n'a pas pu répondre du tout : panne, refus
/// d'autorisation, délai dépassé. LE MESSAGE N'EST ALORS PAS « IGNORÉ », il est
/// perdu, et le journal doit le dire autrement.
/// </param>
public sealed record Adresse(string? Courriel, bool Indisponible = false)
{
    // « Courriel: » EN TOUTES LETTRES, ET CE N'EST PAS DU CONFORT DE LECTURE.
    //
    // Un record engendre aussi un CONSTRUCTEUR DE COPIE — « Adresse(Adresse
    // original) » — accessible depuis l'interieur du type. Avec « new(null) »,
    // c'est lui que le compilateur retient, et il refuse alors le litteral
    // parce que son parametre n'est pas nullable : « Cannot convert null literal
    // to non-nullable reference type ». Nommer l'argument ecarte la copie, dont
    // le parametre s'appelle « original ».
    public static readonly Adresse Absente = new(Courriel: null);

    public static readonly Adresse Injoignable = new(Courriel: null, Indisponible: true);

    public static Adresse De(string? courriel)
        => string.IsNullOrWhiteSpace(courriel) ? Absente : new Adresse(courriel);
}

public interface ISentNotificationRepository
{
    void Add(SentNotification notification);
}
