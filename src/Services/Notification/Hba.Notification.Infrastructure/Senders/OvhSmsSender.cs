using System.Globalization;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Hba.Notification.Application.Ports;
using Hba.Notification.Domain.Messages;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Hba.Notification.Infrastructure.Senders;

/// <summary>
/// Envoi par l'API SMS d'OVHcloud.
///
/// TROIS PIEGES DE CETTE API, TRAITES ICI.
///
/// 1. LA SIGNATURE. OVHcloud ne prend pas un jeton porteur : chaque requête est
///    signée. La formule est stricte, y compris l'ordre des champs et les
///    séparateurs. Une erreur d'un caractère donne un refus qui ne dit pas
///    pourquoi.
///
/// 2. L'HORLOGE. La signature inclut un horodatage, comparé à celui d'OVHcloud.
///    Une machine décalée de quelques minutes fait échouer TOUS les envois,
///    avec la même erreur opaque. On lit donc l'heure du serveur une fois et on
///    conserve l'écart, plutôt que de faire confiance à l'horloge locale.
///
/// 3. UN 200 NE VEUT PAS DIRE ENVOYE. La réponse contient invalidReceivers : un
///    numéro mal formé ou refusé y apparaît alors que le statut HTTP est 200.
///    Le considérer comme un succès ferait croire à un code parti que personne
///    n'a reçu — le pire des cas pour une connexion par OTP.
/// </summary>
public sealed class OvhSmsSender(
    HttpClient http,
    IOptions<OvhSmsOptions> options,
    ILogger<OvhSmsSender> logger) : INotificationSender
{
    private const string ProviderName = "ovh";

    private static readonly JsonSerializerOptions Json = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly OvhSmsOptions _options = options.Value;

    /// <summary>Ecart entre l'horloge locale et celle d'OVHcloud, en secondes.</summary>
    private long? _clockDrift;

    public NotificationChannel Channel => NotificationChannel.Sms;

    public bool IsConfigured => _options.IsConfigured;

    public async Task<SendResult> SendAsync(
        string recipient,
        OutboundMessage message,
        CancellationToken cancellationToken)
    {
        if (message is not TextMessage text)
        {
            return SendResult.Failed(ProviderName, "Un SMS transporte un texte, pas un modèle.");
        }

        var path = $"/sms/{_options.ServiceName}/jobs";
        var url = _options.Endpoint + path;

        var payload = new
        {
            charset = "UTF-8",
            receivers = new[] { recipient },
            message = text.Body,
            priority = "high",
            noStopClause = _options.NoStopClause,
            sender = string.IsNullOrWhiteSpace(_options.Sender) ? null : _options.Sender,
            senderForResponse = string.IsNullOrWhiteSpace(_options.Sender),
        };

        var body = JsonSerializer.Serialize(payload, Json);

        try
        {
            var timestamp = await GetTimestampAsync(cancellationToken).ConfigureAwait(false);

            using var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };

            request.Headers.Add("X-Ovh-Application", _options.ApplicationKey);
            request.Headers.Add("X-Ovh-Consumer", _options.ConsumerKey);
            request.Headers.Add("X-Ovh-Timestamp", timestamp.ToString(CultureInfo.InvariantCulture));
            request.Headers.Add("X-Ovh-Signature", Sign("POST", url, body, timestamp));

            using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "OVHcloud a refusé l'envoi ({Status}) : {Body}",
                    (int)response.StatusCode,
                    content);

                return SendResult.Failed(ProviderName, $"Refus d'OVHcloud, statut {(int)response.StatusCode}.");
            }

            return Interpret(content);
        }
        catch (HttpRequestException ex)
        {
            return SendResult.Failed(ProviderName, $"Appel impossible : {ex.Message}");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return SendResult.Failed(ProviderName, "Delai depasse.");
        }
    }

    /// <summary>
    /// Lit invalidReceivers AVANT de conclure. Un numéro rejeté par OVHcloud
    /// arrive dans une réponse 200 ; sans cette vérification, l'envoi serait
    /// consigné comme réussi et l'utilisateur attendrait un code qui ne
    /// viendra pas.
    /// </summary>
    private SendResult Interpret(string content)
    {
        try
        {
            using var document = JsonDocument.Parse(content);
            var root = document.RootElement;

            if (root.TryGetProperty("invalidReceivers", out var invalid)
                && invalid.ValueKind == JsonValueKind.Array
                && invalid.GetArrayLength() > 0)
            {
                logger.LogWarning("OVHcloud a rejeté le destinataire : {Body}", content);
                return SendResult.Failed(ProviderName, "Numéro refusé par l'opérateur.");
            }

            string? id = null;

            if (root.TryGetProperty("ids", out var ids)
                && ids.ValueKind == JsonValueKind.Array
                && ids.GetArrayLength() > 0)
            {
                id = ids[0].ToString();
            }
            else
            {
                // Ni identifiant, ni destinataire invalide : rien ne prouve
                // qu'un message soit parti.
                logger.LogWarning("Réponse OVHcloud sans identifiant de message : {Body}", content);
                return SendResult.Failed(ProviderName, "Aucun message créé.");
            }

            return SendResult.Ok(ProviderName, id);
        }
        catch (JsonException)
        {
            return SendResult.Failed(ProviderName, "Réponse illisible.");
        }
    }

    /// <summary>
    /// $1$ suivi du SHA-1 de AS+CK+METHOD+QUERY+BODY+TSTAMP. L'ordre et les
    /// séparateurs « + » font partie du contrat : ne pas les réécrire.
    /// </summary>
    private string Sign(string method, string url, string body, long timestamp)
    {
        var material = string.Join(
            '+',
            _options.ApplicationSecret,
            _options.ConsumerKey,
            method,
            url,
            body,
            timestamp.ToString(CultureInfo.InvariantCulture));

        var hash = SHA1.HashData(Encoding.UTF8.GetBytes(material));

        return "$1$" + Convert.ToHexStringLower(hash);
    }

    private async Task<long> GetTimestampAsync(CancellationToken cancellationToken)
    {
        var local = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        if (_clockDrift is not null)
        {
            return local + _clockDrift.Value;
        }

        try
        {
            var server = await http
                .GetFromJsonAsync<long>($"{_options.Endpoint}/auth/time", cancellationToken)
                .ConfigureAwait(false);

            _clockDrift = server - local;

            if (Math.Abs(_clockDrift.Value) > 30)
            {
                logger.LogWarning(
                    "Horloge locale décalée de {Drift} s par rapport à OVHcloud. L'écart est compensé, mais il mérite d'être corrigé.",
                    _clockDrift.Value);
            }

            return server;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            // Pas de raison de renoncer à l'envoi : l'horloge locale est
            // probablement juste. On ne mémorise pas d'écart, pour réessayer
            // la lecture au prochain envoi.
            logger.LogDebug("Heure serveur OVHcloud illisible, horloge locale utilisée.");
            return local;
        }
    }
}
