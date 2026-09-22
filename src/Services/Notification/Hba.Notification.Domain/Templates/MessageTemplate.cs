using System.Text.RegularExpressions;
using Hba.BuildingBlocks.Domain;
using Hba.Notification.Domain.Messages;

namespace Hba.Notification.Domain.Templates;

/// <summary>
/// Modèle de message. Le texte vit dans le code plutôt qu'en base : ces
/// messages sont peu nombreux, ils changent rarement, et une faute de frappe
/// dans un SMS envoyé à des milliers de personnes doit passer par une revue.
///
/// Les variables s'écrivent {nom}. Une variable manquante fait échouer le rendu
/// plutôt que de laisser partir « Votre code est {code} ».
/// </summary>
public sealed partial class MessageTemplate
{
    [GeneratedRegex(@"\{(\w+)\}")]
    private static partial Regex Placeholder();

    internal MessageTemplate(string id, NotificationChannel channel, string body, int maxLength)
    {
        Id = id;
        Channel = channel;
        Body = body;
        MaxLength = maxLength;
    }

    public string Id { get; }

    public NotificationChannel Channel { get; }

    public string Body { get; }

    /// <summary>
    /// Un SMS au-delà de 160 caractères est facturé plusieurs fois. Le dépasser
    /// doit être un choix, pas une surprise en fin de mois.
    /// </summary>
    public int MaxLength { get; }

    public IReadOnlySet<string> Variables =>
        Placeholder().Matches(Body).Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal);

    public string Render(IReadOnlyDictionary<string, string> variables)
    {
        ArgumentNullException.ThrowIfNull(variables);

        var missing = Variables.Where(v => !variables.ContainsKey(v)).ToList();

        if (missing.Count > 0)
        {
            throw new DomainException(
                "MISSING_TEMPLATE_VARIABLE",
                $"Le modèle {Id} attend : {string.Join(", ", missing)}.");
        }

        var rendered = Placeholder().Replace(Body, m => variables[m.Groups[1].Value]);

        if (rendered.Length > MaxLength)
        {
            throw new DomainException(
                "TEMPLATE_TOO_LONG",
                $"Le message rendu depuis {Id} fait {rendered.Length} caractères, au-delà de {MaxLength}.");
        }

        return rendered;
    }
}
