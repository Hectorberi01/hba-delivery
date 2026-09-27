using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Hba.Notification.Application.Ports;
using Hba.Notification.Domain.Messages;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Hba.Notification.Infrastructure.Senders;

/// <summary>
/// Envoi via l'API Cloud de Meta.
///
/// Trois choses que l'API impose et qu'il ne faut pas essayer de contourner :
///
/// LE TEXTE N'EST PAS LE NOTRE. Un modèle d'authentification a un corps fixé
/// par Meta et traduit par Meta selon le code de langue. On envoie un nom de
/// modèle et un code, pas une phrase.
///
/// LE CODE APPARAIT DEUX FOIS — dans le corps et dans le bouton de copie.
/// C'est le format attendu, pas une redite.
///
/// L'OPT-IN EST OBLIGATOIRE avant tout message de gabarit. Cet adaptateur ne
/// le vérifie pas : c'est à l'appelant de ne demander WhatsApp que pour un
/// titulaire qui a consenti.
/// </summary>
public sealed class WhatsAppCloudSender(
    HttpClient http,
    IOptions<WhatsAppOptions> options,
    ILogger<WhatsAppCloudSender> logger) : INotificationSender
{
    private const string ProviderName = "whatsapp-cloud";

    private static readonly JsonSerializerOptions Json = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly WhatsAppOptions _options = options.Value;

    public NotificationChannel Channel => NotificationChannel.WhatsApp;

    public bool IsConfigured => _options.IsConfigured;

    public async Task<SendResult> SendAsync(
        string recipient,
        OutboundMessage message,
        CancellationToken cancellationToken)
    {
        if (message is not WhatsAppAuthenticationMessage authentication)
        {
            // Un texte libre ne part pas sur WhatsApp : hors fenêtre de
            // service, seuls les modèles approuvés sont acceptés par Meta.
            return SendResult.Failed(
                ProviderName,
                "WhatsApp n'accepte qu'un modèle approuvé, pas un texte libre.");
        }

        var payload = new
        {
            messaging_product = "whatsapp",
            to = recipient,
            type = "template",
            template = new
            {
                name = authentication.TemplateName,
                language = new { code = authentication.Language },
                components = new object[]
                {
                    new
                    {
                        type = "body",
                        parameters = new object[]
                        {
                            new { type = "text", text = authentication.Code },
                        },
                    },
                    new
                    {
                        type = "button",
                        sub_type = "url",
                        index = 0,
                        parameters = new object[]
                        {
                            new { type = "text", text = authentication.Code },
                        },
                    },
                },
            },
        };

        var path = $"{_options.GraphApiVersion}/{_options.PhoneNumberId}/messages";

        try
        {
            using var response = await http
                .PostAsJsonAsync(path, payload, Json, cancellationToken)
                .ConfigureAwait(false);

            var body = await response.Content
                .ReadAsStringAsync(cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                // Le corps de l'erreur peut contenir le numéro : il reste dans
                // le journal technique, jamais dans la trace metier.
                logger.LogWarning(
                    "WhatsApp a refusé l'envoi ({Status}) : {Body}",
                    (int)response.StatusCode,
                    body);

                return SendResult.Failed(
                    ProviderName,
                    $"Refus de Meta, statut {(int)response.StatusCode}.");
            }

            return SendResult.Ok(ProviderName, ExtractMessageId(body));
        }
        catch (HttpRequestException ex)
        {
            return SendResult.Failed(ProviderName, $"Appel impossible : {ex.Message}");
        }
        catch (TaskCanceledException)
        {
            // Délai dépassé. L'échec fait basculer sur le repli SMS plutôt que
            // de laisser l'utilisateur devant un écran qui attend.
            return SendResult.Failed(ProviderName, "Delai depasse.");
        }
    }

    /// <summary>
    /// Identifiant du message, utile pour rapprocher un accusé de réception.
    /// Son absence n'est pas un échec : l'envoi a été accepté.
    /// </summary>
    private static string? ExtractMessageId(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);

            if (document.RootElement.TryGetProperty("messages", out var messages)
                && messages.ValueKind == JsonValueKind.Array
                && messages.GetArrayLength() > 0
                && messages[0].TryGetProperty("id", out var id))
            {
                return id.GetString();
            }
        }
        catch (JsonException)
        {
            // Réponse inattendue : l'envoi a réussi, on se passe de
            // l'identifiant plutôt que de transformer cela en échec.
        }

        return null;
    }
}
