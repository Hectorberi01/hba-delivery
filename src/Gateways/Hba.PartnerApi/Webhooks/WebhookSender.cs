using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Hba.BuildingBlocks.Security;

namespace Hba.PartnerApi.Webhooks;

/// <summary>
/// Événements sortants du contrat partenaire. Les noms sont figés : ils font
/// partie de l'API publique.
/// </summary>
public static class WebhookEventTypes
{
    public const string Assigned = "delivery.assigned";
    public const string PickedUp = "delivery.picked_up";
    public const string Delivered = "delivery.delivered";
    public const string Cancelled = "delivery.cancelled";
}

public sealed record WebhookDelivery(
    string PartnerId,
    Uri Endpoint,
    string SigningSecret,
    string EventType,
    string EventId,
    object Payload);

public sealed record WebhookAttempt(
    string EventId,
    int StatusCode,
    bool Succeeded,
    string? Error,
    DateTimeOffset AttemptedAt);

public interface IWebhookSender
{
    Task<WebhookAttempt> SendAsync(WebhookDelivery delivery, CancellationToken cancellationToken);
}

/// <summary>
/// Envoie un webhook signé en HMAC-SHA256. Le partenaire vérifie la signature
/// avec son secret : c'est ce qui distingue un appel de HBA d'un appel forgé.
/// </summary>
public sealed class WebhookSender(HttpClient http, ILogger<WebhookSender> logger) : IWebhookSender
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<WebhookAttempt> SendAsync(WebhookDelivery delivery, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(delivery);

        var sentAt = DateTimeOffset.UtcNow;
        var body = JsonSerializer.Serialize(
            new
            {
                id = delivery.EventId,
                type = delivery.EventType,
                sentAt,
                data = delivery.Payload,
            },
            JsonOptions);

        using var request = new HttpRequestMessage(HttpMethod.Post, delivery.Endpoint)
        {
            Content = new StringContent(body, Encoding.UTF8),
        };

        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        request.Headers.TryAddWithoutValidation(
            "X-HBA-Signature",
            HmacSignature.Compute(delivery.SigningSecret, body, sentAt));
        request.Headers.TryAddWithoutValidation("X-HBA-Event-Id", delivery.EventId);
        request.Headers.TryAddWithoutValidation("X-HBA-Event-Type", delivery.EventType);
        request.Headers.TryAddWithoutValidation(
            "X-HBA-Timestamp",
            sentAt.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture));

        try
        {
            using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);

            var succeeded = response.IsSuccessStatusCode;

            if (!succeeded)
            {
                logger.LogWarning(
                    "Webhook {EventType} refusé par {PartnerId} : {StatusCode}.",
                    delivery.EventType,
                    delivery.PartnerId,
                    (int)response.StatusCode);
            }

            return new WebhookAttempt(
                delivery.EventId,
                (int)response.StatusCode,
                succeeded,
                succeeded ? null : response.ReasonPhrase,
                sentAt);
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Webhook {EventType} injoignable chez {PartnerId}.", delivery.EventType, delivery.PartnerId);
            return new WebhookAttempt(delivery.EventId, 0, false, ex.Message, sentAt);
        }
        catch (TaskCanceledException ex)
        {
            logger.LogWarning(ex, "Webhook {EventType} expiré chez {PartnerId}.", delivery.EventType, delivery.PartnerId);
            return new WebhookAttempt(delivery.EventId, 0, false, "timeout", sentAt);
        }
    }
}
