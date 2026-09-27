using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Hba.Payment.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Hba.Payment.Infrastructure.Services.FedaPay;

/// <summary>
/// Verification de la signature des notifications FedaPay.
///
/// LE FORMAT N'EST PAS DANS LA DOCUMENTATION PUBLIQUE : il est repris des
/// bibliotheques officielles du fournisseur (WebhookSignature). L'en-tete
/// « X-FEDAPAY-SIGNATURE » vaut « t=&lt;horodatage unix&gt;,s=&lt;signature&gt; »,
/// eventuellement avec plusieurs « s= » lors d'une rotation de secret ; la
/// chaine signee est « &lt;horodatage&gt;.&lt;corps brut&gt; », en HMAC-SHA-256
/// hexadecimal.
///
/// LE CORPS DOIT ETRE CELUI RECU, OCTET POUR OCTET. Reserialiser le JSON avant
/// de le signer changerait un espace ou l'ordre d'une cle, et toutes les
/// notifications seraient rejetees.
/// </summary>
internal sealed class FedaPayWebhookVerifier(
    IOptions<FedaPayOptions> options,
    ILogger<FedaPayWebhookVerifier> logger) : IWebhookVerifier
{
    private const string Scheme = "s";
    private const string TimestampKey = "t";

    private readonly FedaPayOptions _options = options.Value;

    public string? ReadTransactionReference(string rawBody, string? signatureHeader)
    {
        if (string.IsNullOrWhiteSpace(_options.WebhookSecret))
        {
            // REFUSER PLUTOT QUE LAISSER PASSER. Sans secret, aucune
            // notification n'est authentifiable : les accepter reviendrait a
            // laisser n'importe qui marquer une course comme payee.
            logger.LogError("Aucun secret de webhook configure : la notification est refusee.");
            return null;
        }

        if (string.IsNullOrWhiteSpace(signatureHeader) || string.IsNullOrEmpty(rawBody))
        {
            return null;
        }

        if (!TryParseHeader(signatureHeader, out var timestamp, out var signatures))
        {
            logger.LogWarning("En-tete de signature illisible.");
            return null;
        }

        var age = Math.Abs(DateTimeOffset.UtcNow.ToUnixTimeSeconds() - timestamp);
        if (age > _options.WebhookToleranceSeconds)
        {
            logger.LogWarning(
                "Notification hors tolerance : {Age}s d'ecart pour un maximum de {Tolerance}s.",
                age,
                _options.WebhookToleranceSeconds);
            return null;
        }

        var expected = Compute($"{timestamp.ToString(CultureInfo.InvariantCulture)}.{rawBody}");

        // Comparaison a temps constant : une comparaison ordinaire fuit, par sa
        // duree, le nombre d'octets corrects en tete.
        var matches = signatures.Any(candidate => CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(candidate),
            Encoding.UTF8.GetBytes(expected)));

        if (!matches)
        {
            logger.LogWarning("Signature de notification invalide.");
            return null;
        }

        return ReadReference(rawBody);
    }

    private static bool TryParseHeader(string header, out long timestamp, out List<string> signatures)
    {
        timestamp = 0;
        signatures = [];

        foreach (var item in header.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = item.IndexOf('=', StringComparison.Ordinal);
            if (separator <= 0)
            {
                continue;
            }

            var key = item[..separator];
            var value = item[(separator + 1)..];

            if (key == TimestampKey && long.TryParse(value, CultureInfo.InvariantCulture, out var parsed))
            {
                timestamp = parsed;
            }
            else if (key == Scheme && value.Length > 0)
            {
                signatures.Add(value);
            }
        }

        return timestamp > 0 && signatures.Count > 0;
    }

    private string Compute(string signedPayload)
    {
        var key = Encoding.UTF8.GetBytes(_options.WebhookSecret);
        var hash = HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(signedPayload));
        return Convert.ToHexStringLower(hash);
    }

    /// <summary>
    /// Ne lit du corps QUE l'identifiant de la transaction. Le verdict, lui,
    /// sera relu chez le fournisseur : c'est la seule chose qu'on ne deduit
    /// jamais d'un message entrant.
    /// </summary>
    private string? ReadReference(string rawBody)
    {
        try
        {
            using var document = JsonDocument.Parse(rawBody);
            var root = document.RootElement;

            if (root.TryGetProperty("entity", out var entity) && TryReadId(entity, out var entityId))
            {
                return entityId;
            }

            if (root.TryGetProperty("object", out var legacy) && TryReadId(legacy, out var legacyId))
            {
                return legacyId;
            }

            if (root.TryGetProperty("object_id", out var objectId))
            {
                return objectId.ToString();
            }

            logger.LogWarning("Notification signee mais sans identifiant de transaction exploitable.");
            return null;
        }
        catch (JsonException exception)
        {
            logger.LogWarning(exception, "Corps de notification illisible.");
            return null;
        }
    }

    private static bool TryReadId(JsonElement element, out string id)
    {
        id = string.Empty;

        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty("id", out var value))
        {
            return false;
        }

        id = value.ToString();
        return !string.IsNullOrWhiteSpace(id);
    }
}
