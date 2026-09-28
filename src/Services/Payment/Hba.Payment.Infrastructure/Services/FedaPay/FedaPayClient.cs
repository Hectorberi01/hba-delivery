using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Hba.BuildingBlocks.Domain;
using Hba.Payment.Application.Common.Interfaces;
using Hba.Payment.Domain.Exceptions;
using Hba.Payment.Domain.Payments;
using Hba.Payment.Domain.ValueObjects;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Hba.Payment.Infrastructure.Services.FedaPay;

/// <summary>
/// Adaptateur FedaPay.
///
/// LE PARCOURS RETENU EST LA PAGE DE PAIEMENT HEBERGEE, en deux appels : on
/// cree une transaction, puis on demande son jeton, qui rend l'adresse ou
/// envoyer le payeur. L'alternative — pousser directement une demande sur le
/// telephone — oblige a choisir l'operateur (MTN, Moov, Celtiis) transaction
/// par transaction, information que l'application ne collecte pas et que le
/// client lui-meme confond souvent. La page hebergee couvre les trois, plus la
/// carte, avec une seule integration.
///
/// AUCUN DEBIT N'EST POSSIBLE SANS LE CLIENT : chez FedaPay comme chez MTN
/// MoMo, le payeur valide sur son telephone avec son code PIN. Il n'existe ni
/// prelevement serveur, ni mandat pre-autorise — c'est ce qui rend le paiement
/// d'un partenaire impossible par cette voie (point 2 des points a trancher).
/// </summary>
internal sealed class FedaPayClient(HttpClient http, IOptions<FedaPayOptions> options, ILogger<FedaPayClient> logger) : IPaymentProvider
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Noms sous lesquels l'adresse de paiement peut arriver.</summary>
    private static readonly string[] UrlFields = ["payment_url", "url"];

    private readonly FedaPayOptions _options = options.Value;

    public string Name => _options.IsLive ? "fedapay (live)" : "fedapay (sandbox)";

    public async Task<ProviderCheckout> OpenCheckoutAsync(
        Guid paymentIntentId,
        Guid deliveryId,
        MoneyXof amount,
        string payerPhone,
        string? returnUrl,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(amount);

        var request = new Dictionary<string, object?>
        {
            ["description"] = $"Course HBA {deliveryId}",

            // ENTIER, SANS CENTIMES. Le XOF n'a pas de subdivision : 1180
            // signifie 1180 FCFA, et toute multiplication par cent facturerait
            // cent fois le prix.
            ["amount"] = amount.Amount,
            ["currency"] = new Dictionary<string, string> { ["iso"] = MoneyXof.CurrencyCode },

            // C'est ce qui permet a la finance de rapprocher une ligne FedaPay
            // d'une course sans passer par nous.
            ["custom_metadata"] = new Dictionary<string, string>
            {
                ["payment_intent_id"] = paymentIntentId.ToString(),
                ["delivery_id"] = deliveryId.ToString(),
            },
        };

        // Un callback_url vide n'est pas un callback_url : on omet la cle
        // plutot que d'envoyer null, que le fournisseur refuse.
        var callback = string.IsNullOrWhiteSpace(returnUrl) ? _options.CallbackUrl : returnUrl;
        if (!string.IsNullOrWhiteSpace(callback))
        {
            request["callback_url"] = callback;
        }

        if (!string.IsNullOrWhiteSpace(payerPhone))
        {
            request["customer"] = new Dictionary<string, object?>
            {
                ["phone_number"] = new Dictionary<string, string>
                {
                    ["number"] = payerPhone,
                    ["country"] = _options.CustomerCountry,
                },
            };
        }

        var transaction = await PostAsync("transactions", request, cancellationToken).ConfigureAwait(false);
        var reference = ReadTransactionId(transaction);

        // LA CREATION REND DEJA « payment_url ». Un second appel au point
        // /token rendrait la meme adresse : le faire systematiquement doublait
        // la latence d'ouverture d'un paiement, et donc le risque de depasser
        // l'echeance que Delivery nous accorde. Il ne reste qu'en repli, au
        // cas ou une reponse ne porterait pas l'adresse.
        var checkoutUrl = ReadUrl(transaction);

        if (checkoutUrl is null)
        {
            var token = await PostAsync($"transactions/{reference}/token", null, cancellationToken)
                .ConfigureAwait(false);

            checkoutUrl = ReadUrl(token);
        }

        if (checkoutUrl is null)
        {
            throw new DomainException(
                PaymentErrorCodes.ProviderRejected,
                "Le fournisseur n'a pas rendu d'adresse de paiement.");
        }

        logger.LogInformation(
            "Transaction {Reference} ouverte chez FedaPay pour la livraison {DeliveryId} ({Amount} XOF).",
            reference,
            deliveryId,
            amount.Amount);

        return new ProviderCheckout(reference, checkoutUrl);
    }

    public async Task<ProviderPayment> GetPaymentAsync(
        string providerReference,
        CancellationToken cancellationToken)
    {
        using var document = await SendAsync(
            new HttpRequestMessage(HttpMethod.Get, $"transactions/{providerReference}"),
            cancellationToken).ConfigureAwait(false);

        var transaction = Unwrap(document, "transaction");
        var raw = transaction.TryGetProperty("status", out var status)
            ? status.GetString() ?? string.Empty
            : string.Empty;

        return new ProviderPayment(MapStatus(raw), raw, ReadFailureReason(transaction, raw));
    }

    /// <summary>
    /// Les libelles viennent de la bibliotheque officielle du fournisseur : y
    /// sont consideres payes « approved », « transferred », « refunded » et
    /// leurs variantes partiellement remboursees.
    ///
    /// UN STATUT INCONNU RESTE EN ATTENTE, JAMAIS EN ECHEC. Le fournisseur peut
    /// en ajouter un demain ; le prendre pour un refus annulerait des courses
    /// payees, alors que le prendre pour une attente ne fait que reporter la
    /// decision au prochain rappel.
    /// </summary>
    private static PaymentStatus MapStatus(string raw) => raw switch
    {
        "approved" or "transferred" => PaymentStatus.Succeeded,
        "refunded" => PaymentStatus.Refunded,
        "approved_partially_refunded" or "transferred_partially_refunded" => PaymentStatus.PartiallyRefunded,
        "declined" or "canceled" or "cancelled" => PaymentStatus.Failed,
        _ => PaymentStatus.Pending,
    };

    private static string? ReadFailureReason(JsonElement transaction, string raw)
    {
        if (MapStatus(raw) != PaymentStatus.Failed)
        {
            return null;
        }

        return transaction.TryGetProperty("last_error_code", out var code) && code.ValueKind == JsonValueKind.String
            ? $"{raw} ({code.GetString()})"
            : raw;
    }

    private async Task<JsonElement> PostAsync(
        string path,
        IReadOnlyDictionary<string, object?>? body,
        CancellationToken cancellationToken)
    {
        var message = new HttpRequestMessage(HttpMethod.Post, path);
        if (body is not null)
        {
            message.Content = JsonContent.Create(body, options: Json);
        }

        using var document = await SendAsync(message, cancellationToken).ConfigureAwait(false);
        return Unwrap(document, "transaction");
    }

    private async Task<JsonDocument> SendAsync(HttpRequestMessage message, CancellationToken cancellationToken)
    {
        using var request = message;

        HttpResponseMessage response;

        try
        {
            response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException exception)
        {
            // INJOIGNABLE N'EST PAS REFUSE. L'un se reessaie, l'autre non : les
            // confondre ferait echouer definitivement une course a cause d'une
            // coupure reseau de trois secondes.
            throw new DomainException(
                PaymentErrorCodes.ProviderUnavailable,
                "Le service de paiement est momentanement injoignable.",
                exception);
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new DomainException(
                PaymentErrorCodes.ProviderUnavailable,
                "Le service de paiement n'a pas repondu dans le delai imparti.",
                exception);
        }

        using (response)
        {
            var payload = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                // LE CORPS D'ERREUR EST JOURNALISE EN ENTIER. C'est la
                // premiere integration avec ce fournisseur : sans son message,
                // un 422 ne dit pas quel champ il refuse, et le diagnostic se
                // fait a l'aveugle.
                logger.LogError(
                    "FedaPay a refuse {Method} {Path} avec {Status} : {Payload}",
                    request.Method,
                    request.RequestUri,
                    (int)response.StatusCode,
                    payload);

                throw new DomainException(
                    PaymentErrorCodes.ProviderRejected,
                    "Le service de paiement a refuse la demande.");
            }

            return JsonDocument.Parse(payload);
        }
    }

    /// <summary>
    /// Deballe l'enveloppe des reponses du fournisseur.
    ///
    /// LA CLE EST PREFIXEE PAR LA VERSION : une creation de transaction rend
    /// <c>{ "v1/transaction": { … } }</c>, pas <c>{ "transaction": … }</c>.
    /// Le nom non prefixe est celui des bibliotheques clientes, qui le
    /// normalisent avant de le rendre — le deduire d'un SDK plutot que d'une
    /// reponse reelle conduit a chercher un champ « id » sur l'enveloppe, a ne
    /// pas le trouver, et a rejeter une transaction parfaitement creee.
    ///
    /// Le point <c>/token</c>, lui, rend <c>{ "token": …, "url": … }</c> sans
    /// enveloppe : d'ou le repli sur la racine.
    /// </summary>
    private static JsonElement Unwrap(JsonDocument document, string key)
    {
        var root = document.RootElement;

        if (root.TryGetProperty($"v1/{key}", out var versionne))
        {
            return versionne.Clone();
        }

        return root.TryGetProperty(key, out var nu) ? nu.Clone() : root.Clone();
    }

    /// <summary>
    /// Adresse de la page de paiement. Elle s'appelle <c>payment_url</c> dans
    /// l'objet transaction et <c>url</c> dans la reponse du point /token.
    /// </summary>
    private static string? ReadUrl(JsonElement element)
    {
        foreach (var nom in UrlFields)
        {
            if (element.TryGetProperty(nom, out var valeur)
                && valeur.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(valeur.GetString()))
            {
                return valeur.GetString();
            }
        }

        return null;
    }

    private static string ReadTransactionId(JsonElement transaction)
    {
        if (!transaction.TryGetProperty("id", out var id))
        {
            throw new DomainException(
                PaymentErrorCodes.ProviderRejected,
                "Le fournisseur n'a pas rendu d'identifiant de transaction.");
        }

        return id.ValueKind == JsonValueKind.Number
            ? id.GetInt64().ToString(CultureInfo.InvariantCulture)
            : id.GetString() ?? throw new DomainException(
                PaymentErrorCodes.ProviderRejected,
                "Identifiant de transaction illisible.");
    }
}
