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
/// LE PARCOURS RETENU EST LA PAGE DE PAIEMENT HEBERGEE, en un appel : on cree
/// la transaction, et sa reponse porte deja l'adresse ou envoyer le payeur. Il
/// y choisit son operateur — MTN, Moov ou Celtiis — et son numero, puis valide
/// avec son code PIN.
///
/// CE PARCOURS A ETE RETIRE LE 3 OCTOBRE 2026 AU MATIN ET REMIS LE MEME JOUR.
/// L'idee etait de pousser la demande directement sur le telephone, sans rien a
/// ouvrir. Le chemin existe bien — POST /v1/mtn_open, A LA RACINE, pas sous
/// transactions/ — mais le fournisseur le refuse sur ce compte :
///
///     400 {"message":"Operation non autorisee","errors":{},"model":null}
///
/// SON CORPS D'ERREUR EST VIDE, ET C'EST CE QUI TRANCHE. Quand c'est la requete
/// qui cloche, FedaPay nomme le champ fautif ; quand il ne nomme rien, c'est
/// l'operation elle-meme qui n'est pas permise — une methode de collecte non
/// ouverte cote fournisseur. Aucune ecriture ne leve cela.
///
/// POUR REESSAYER UN JOUR : faire activer MTN et Moov sur le compte marchand,
/// puis relancer scripts/fedapay-sonde-poussee.sh, qui separe en trois essais
/// l'operateur, la completude du client et le compte.
///
/// AUCUN DEBIT N'EST POSSIBLE SANS LE CLIENT : chez FedaPay comme chez MTN
/// MoMo, le payeur valide sur son telephone avec son code PIN. Il n'existe ni
/// prelevement serveur, ni mandat pre-autorise — c'est ce qui rend le paiement
/// d'un partenaire impossible par cette voie (point 2 des points a trancher).
/// </summary>
internal sealed class FedaPayClient(HttpClient http, IOptions<FedaPayOptions> options, ILogger<FedaPayClient> logger) : IPaymentProvider
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Noms sous lesquels l'adresse de la page peut arriver.</summary>
    ///
    /// <remarks>
    /// DEUX NOMS POUR LA MEME CHOSE, selon le point interroge : la creation
    /// d'une transaction la rend sous « payment_url », le point /token sous
    /// « url ». Chercher les deux evite de dependre de celui qui a repondu.
    /// </remarks>
    private static readonly string[] UrlFields = ["payment_url", "url"];

    private static readonly (string Indicatif, string Iso)[] Indicatifs =
    [
        ("+229", "bj"),
        ("+225", "ci"),
        ("+226", "bf"),
        ("+221", "sn"),
        ("+228", "tg"),
        ("+223", "ml"),
        ("+227", "ne"),
        ("+245", "gw"),
    ];

    private readonly FedaPayOptions _options = options.Value;

    public string Name => _options.IsLive ? "fedapay (live)" : "fedapay (sandbox)";

    public async Task<ProviderCheckout> OpenCheckoutAsync(
        Guid paymentIntentId,
        ServiceType serviceType,
        Guid referenceId,
        MoneyXof amount,
        string payerPhone,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(amount);

        var request = new Dictionary<string, object?>
        {
            ["description"] = Libelle(serviceType, referenceId),

            // ENTIER, SANS CENTIMES. Le XOF n'a pas de subdivision : 1180
            // signifie 1180 FCFA, et toute multiplication par cent facturerait
            // cent fois le prix.
            ["amount"] = amount.Amount,
            ["currency"] = new Dictionary<string, string> { ["iso"] = MoneyXof.CurrencyCode },

            // C'EST PAR LA QUE FINANCE RAPPROCHE. Verifie au tableau de bord du
            // fournisseur le 3 octobre 2026 : ces trois cles y apparaissent
            // telles quelles sur chaque transaction, et elles suffisent a
            // retrouver la mission sans passer par nous.
            ["custom_metadata"] = new Dictionary<string, string>
            {
                ["payment_intent_id"] = paymentIntentId.ToString(),
                ["service_type"] = serviceType.ToString().ToLowerInvariant(),
                ["reference_id"] = referenceId.ToString(),
            },
        };

        // Un callback_url vide n'est pas un callback_url : on omet la cle
        // plutot que d'envoyer null, que le fournisseur refuse.
        if (!string.IsNullOrWhiteSpace(_options.CallbackUrl))
        {
            request["callback_url"] = _options.CallbackUrl;
        }

        // UN CLIENT DE PLUS CHEZ LE FOURNISSEUR A CHAQUE MISSION, et c'est le
        // prix de ce qui suit. Verifie le 29 septembre 2026 : deux creations
        // portant le MEME numero produisent deux « customer_id » differents —
        // le fournisseur ne rapproche pas sur le telephone, il rapproche sur
        // l'ADRESSE DE COURRIEL, que nous n'envoyons pas. Sa console affiche
        // donc « Unknown Unknown » sur chacune de nos lignes, ce que le tableau
        // de bord a confirme le 3 octobre.
        //
        // CE N'EST PAS UN OUBLI QU'ON CORRIGE EN UNE LIGNE. Payment ne detient
        // que l'identifiant du client ; son nom et son courriel vivent dans
        // Identity, et ce depot refuse deliberement d'appeler Identity dans le
        // chemin de paiement — Identity injoignable, et plus personne ne peut
        // commander. Le rapprochement passe donc par « custom_metadata ».
        //
        // LE NUMERO N'EST TRANSMIS QUE S'IL EST DU PAYS OU L'ON ENCAISSE. Il ne
        // sert qu'a preremplir la page : le payeur choisit de toute facon son
        // operateur et son numero sur l'ecran du fournisseur. En transmettre un
        // qui ne peut pas etre debite est sans benefice et avec un cout — celui
        // d'une transaction qui echoue sans motif lisible.
        var paysDuPayeur = PaysDuNumero(payerPhone);

        if (paysDuPayeur is not null
            && string.Equals(paysDuPayeur, _options.CustomerCountry, StringComparison.OrdinalIgnoreCase))
        {
            request["customer"] = new Dictionary<string, object?>
            {
                ["phone_number"] = new Dictionary<string, string>
                {
                    ["number"] = payerPhone,
                    ["country"] = paysDuPayeur,
                },
            };
        }
        else if (!string.IsNullOrWhiteSpace(payerPhone))
        {
            // LE NUMERO N'APPARAIT PAS DANS CETTE LIGNE. C'est une donnee
            // personnelle, et le pays deduit suffit a comprendre.
            logger.LogInformation(
                "Numero du payeur non transmis : pays deduit {Pays}, encaissement en {Encaissement}.",
                paysDuPayeur ?? "inconnu",
                _options.CustomerCountry);
        }

        var transaction = await PostAsync("transactions", request, cancellationToken).ConfigureAwait(false);
        var reference = ReadTransactionId(transaction);

        // LA CREATION REND DEJA « payment_url ». Un second appel au point
        // /token rendrait la meme adresse : le faire systematiquement doublait
        // la latence d'ouverture d'un paiement, et donc le risque de depasser
        // l'echeance que l'appelant nous accorde. Il ne reste qu'en repli, au
        // cas ou une reponse ne porterait pas l'adresse.
        //
        // VERIFIE LE 29 SEPTEMBRE 2026, ET IL AVAIT ETE MIS EN DOUTE DEUX FOIS.
        // La documentation du fournisseur ne decrit l'adresse que sur le point
        // /token, et une lecture A POSTERIORI d'une transaction rend
        // « payment_url: null » — deux raisons de croire ce commentaire faux.
        // Trois creations reelles ont tranche : la reponse de creation porte
        // « payment_url » ET « payment_token ». Le repli n'a jamais servi.
        var checkoutUrl = ReadUrl(transaction);

        if (checkoutUrl is null)
        {
            var jeton = await PostAsync($"transactions/{reference}/token", null, cancellationToken)
                .ConfigureAwait(false);

            checkoutUrl = ReadUrl(jeton);
        }

        if (checkoutUrl is null)
        {
            throw new DomainException(
                PaymentErrorCodes.ProviderRejected,
                "Le fournisseur n'a pas rendu d'adresse de paiement.");
        }

        logger.LogInformation(
            "Transaction {Reference} ouverte chez FedaPay pour la mission {Nature} {ReferenceId} ({Amount} XOF).",
            reference,
            serviceType,
            referenceId,
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
    /// Adresse de la page de paiement, sous celui de ses deux noms qui est
    /// present.
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

    private static string? PaysDuNumero(string? numero)
    {
        if (string.IsNullOrWhiteSpace(numero))
        {
            return null;
        }

        foreach (var (indicatif, iso) in Indicatifs)
        {
            if (numero.StartsWith(indicatif, StringComparison.Ordinal))
            {
                return iso;
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

    /// <summary>
    /// Ce que le payeur lit sur la demande qui s'affiche sur son telephone,
    /// puis sur son releve.
    /// </summary>
    private static string Libelle(ServiceType serviceType, Guid referenceId) => serviceType switch
    {
        ServiceType.Ride => $"Course HBA {referenceId}",
        _ => $"Livraison HBA {referenceId}",
    };
}
