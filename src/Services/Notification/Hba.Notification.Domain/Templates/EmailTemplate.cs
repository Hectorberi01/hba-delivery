namespace Hba.Notification.Domain.Templates;

/// <summary>Un objet et un corps, rendus.</summary>
public sealed record RenderedEmail(string Subject, string Body);

/// <summary>
/// Modèle de courriel : deux textes, pas un.
/// </summary>
///
/// <remarks>
/// L'OBJET EST UN MODELE A PART ENTIERE, et non une chaîne posée à côté. Il
/// porte des variables comme le corps — « Votre course {reference} » — et il
/// doit donc échouer de la même façon quand l'une manque. Réutiliser
/// MessageTemplate pour les deux fait que cette règle n'est écrite qu'une fois.
///
/// LES LONGUEURS MAXIMALES NE SONT PAS COSMETIQUES. Un objet au-delà d'environ
/// quatre-vingts caractères est coupé par les clients de messagerie mobiles,
/// et ce qui compte se retrouve hors champ ; le plafond ici est plus large,
/// pour attraper l'accident, pas pour cadrer le style.
/// </remarks>
public sealed class EmailTemplate
{
    /// <summary>Au-delà, l'objet est tronqué par la plupart des clients.</summary>
    public const int ObjetMaximal = 150;

    /// <summary>
    /// Plafond du corps. GENEREUX, MAIS PAS ABSENT : un modèle qui dépasse
    /// signale une variable qui a ramené autre chose que ce qu'on croyait.
    /// </summary>
    public const int CorpsMaximal = 5000;

    internal EmailTemplate(string id, string subject, string body)
    {
        Id = id;
        Subject = new MessageTemplate(id + ".subject", Messages.NotificationChannel.Email, subject, ObjetMaximal);
        Body = new MessageTemplate(id + ".body", Messages.NotificationChannel.Email, body, CorpsMaximal);
    }

    public string Id { get; }

    public MessageTemplate Subject { get; }

    public MessageTemplate Body { get; }

    public RenderedEmail Render(IReadOnlyDictionary<string, string> variables)
        => new(Subject.Render(variables), Body.Render(variables));
}
