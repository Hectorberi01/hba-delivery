using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Hba.Notification.Application.Ports;
using Hba.Notification.Domain.Messages;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Hba.Notification.Infrastructure.Senders;

/// <summary>
/// Réglages de Resend.
/// </summary>
///
/// <remarks>
/// LE DOMAINE DOIT ETRE VERIFIE CHEZ RESEND AVANT QUE QUOI QUE CE SOIT PARTE,
/// et c'est la panne numéro un de ce fournisseur. Tant que les enregistrements
/// DNS (DKIM, SPF) de hbatechettrade.com ne sont pas posés et validés dans le
/// tableau de bord, l'API répond 403 « The domain is not verified » pour
/// CHAQUE envoi. Avant vérification, Resend n'autorise que l'expéditeur
/// onboarding@resend.dev, et uniquement vers l'adresse du titulaire du compte :
/// un essai qui « marche » dans ces conditions ne prouve rien sur la
/// production.
///
/// LA CLE D'API EST UN SECRET D'ENVOI. Elle permet d'expédier au nom du
/// domaine : elle ne vit ni dans appsettings.json, ni dans le dépôt.
/// </remarks>
public sealed class ResendOptions
{
    public const string SectionName = "Resend";

    /// <summary>Clé d'API, de la forme « re_… ».</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Expéditeur, « HBA Delivery &lt;recus@hbatechettrade.com&gt; » ou une
    /// adresse nue. Le domaine doit être vérifié.
    /// </summary>
    public string From { get; set; } = string.Empty;

    /// <summary>
    /// Adresse de réponse.
    /// </summary>
    ///
    /// <remarks>
    /// LE MODELE DU REÇU INVITE LE CLIENT A REPONDRE. Si l'expéditeur est une
    /// boîte que personne ne relève, sa réponse tombe dans le vide — et il
    /// conclut que HBA ne répond pas. Mettez ici l'adresse du support.
    /// </remarks>
    public string ReplyTo { get; set; } = string.Empty;

    public int TimeoutSeconds { get; set; } = 20;

    public bool EstConfigure =>
        !string.IsNullOrWhiteSpace(ApiKey) && !string.IsNullOrWhiteSpace(From);
}

/// <summary>
/// Envoie un courriel par l'API HTTP de Resend.
/// </summary>
///
/// <remarks>
/// POURQUOI L'API PLUTOT QUE LE RELAIS SMTP DE RESEND, qui existe et que
/// l'adaptateur SMTP d'à côté saurait déjà utiliser. Trois choses que SMTP ne
/// donne pas :
///
///   - L'IDENTIFIANT DU MESSAGE, rendu par l'API. C'est ce qu'on colle dans un
///     ticket quand un client dit n'avoir rien reçu ; sans lui, on cherche dans
///     un tableau de bord à l'heure approximative.
///   - LA CLE D'IDEMPOTENCE, honorée vingt-quatre heures. Elle referme la
///     fenêtre où un envoi réussi suivi d'un échec de l'Inbox ferait partir le
///     reçu deux fois.
///   - DES ERREURS QUI DESIGNENT LEUR CAUSE. Un domaine non vérifié donne un
///     403 nommé ; par SMTP, le même refus arrive en « 550 » opaque.
///
/// L'ADAPTATEUR SMTP RESTE, et ce n'est pas de l'indécision : c'est l'issue de
/// secours le jour où Resend est indisponible ou refuse le domaine. Email:Provider
/// choisit, et le service annonce lequel au démarrage.
///
/// LE CORPS EST DU TEXTE BRUT. Un reçu tient en huit lignes ; en faire une page
/// HTML ajouterait une mise en page à tester dans vingt clients de messagerie,
/// et ferait basculer le message dans les indésirables plus souvent
/// qu'autrement.
/// </remarks>
public sealed class ResendEmailSender(
    HttpClient client,
    IOptions<ResendOptions> options,
    ILogger<ResendEmailSender> journal) : INotificationSender
{
    private const string Route = "emails";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly ResendOptions _options = options.Value;

    public NotificationChannel Channel => NotificationChannel.Email;

    public bool IsConfigured => _options.EstConfigure;

    public async Task<SendResult> SendAsync(
        string recipient,
        OutboundMessage message,
        CancellationToken cancellationToken)
    {
        if (message is not EmailMessage courriel)
        {
            // LE CANAL ET LE MESSAGE SE SONT DESACCORDES : c'est un bug du
            // rendu, pas une panne du fournisseur. Le dire ainsi evite de
            // chercher du cote de Resend pendant une heure.
            return SendResult.Failed(
                "resend",
                $"Message de type {message?.GetType().Name} sur le canal courriel.");
        }

        var corps = new DemandeResend
        {
            From = _options.From,
            To = [recipient],
            Subject = courriel.Subject,
            Text = courriel.Body,
            ReplyTo = string.IsNullOrWhiteSpace(_options.ReplyTo) ? null : _options.ReplyTo,
        };

        using var requete = new HttpRequestMessage(HttpMethod.Post, Route)
        {
            Content = JsonContent.Create(corps, options: Json),
        };

        // LA CLE NE PART QUE SI ELLE EXISTE ET TIENT DANS LA LIMITE. Resend
        // refuse la requete entiere au-dela de 256 caracteres : envoyer une cle
        // trop longue ferait echouer un envoi que l'absence de cle aurait laisse
        // passer. Nos identifiants font 36 caracteres ; le controle est la pour
        // le jour ou quelqu'un y mettra autre chose.
        if (courriel.IdempotencyKey is { Length: > 0 and <= 256 } cle)
        {
            requete.Headers.TryAddWithoutValidation("Idempotency-Key", cle);
        }

        try
        {
            using var reponse = await client.SendAsync(requete, cancellationToken).ConfigureAwait(false);
            var texte = await reponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (reponse.IsSuccessStatusCode)
            {
                return SendResult.Ok("resend", Identifiant(texte));
            }

            return Refus(reponse, texte);
        }
        catch (HttpRequestException erreur)
        {
            journal.LogWarning(erreur, "Resend injoignable.");
            return SendResult.Failed("resend", $"Resend injoignable : {erreur.Message}");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return SendResult.Failed("resend", $"Resend n'a pas repondu en {_options.TimeoutSeconds} s.");
        }
    }

    /// <summary>
    /// Traduit un refus de Resend en message exploitable.
    /// </summary>
    ///
    /// <remarks>
    /// LES TROIS CAS QU'ON RENCONTRERA VRAIMENT SONT NOMMES. Un 403 sur un
    /// domaine non verifie est la panne numero un de ce fournisseur, et
    /// « validation_error » seul n'aide personne a la trouver ; un 401 veut
    /// dire que la cle manque ou a ete revoquee ; un 429 passera tout seul.
    /// </remarks>
    private SendResult Refus(HttpResponseMessage reponse, string corps)
    {
        var erreur = Lire(corps);
        var detail = string.IsNullOrWhiteSpace(erreur) ? corps : erreur;

        switch (reponse.StatusCode)
        {
            case HttpStatusCode.Unauthorized:
                journal.LogError(
                    "Resend refuse la cle d'API : {Detail}. Verifiez RESEND_API_KEY.",
                    detail);
                break;

            case HttpStatusCode.Forbidden:
                journal.LogError(
                    "Resend refuse l'expediteur {Expediteur} : {Detail}. "
                    + "Le domaine doit etre verifie dans le tableau de bord Resend (DKIM et SPF).",
                    _options.From,
                    detail);
                break;

            case HttpStatusCode.TooManyRequests:
                // RETRY-AFTER EST INDICATIF ICI : on ne rejoue pas dans cette
                // methode. L'echec remonte, la trace est ecrite, et c'est
                // l'Inbox qui refera passer le message.
                var apres = reponse.Headers.RetryAfter?.Delta?.TotalSeconds;
                journal.LogWarning(
                    "Resend limite le debit ({Detail}). Nouvelle tentative possible dans {Secondes} s.",
                    detail,
                    apres ?? 1);
                break;

            default:
                journal.LogWarning("Resend a refuse le message ({Statut}) : {Detail}.", (int)reponse.StatusCode, detail);
                break;
        }

        return SendResult.Failed("resend", $"{(int)reponse.StatusCode} : {detail}");
    }

    /// <summary>L'identifiant rendu par Resend, ou null si la reponse a change.</summary>
    private static string? Identifiant(string corps)
    {
        try
        {
            using var document = JsonDocument.Parse(corps);
            return document.RootElement.TryGetProperty("id", out var id) ? id.GetString() : null;
        }
        catch (JsonException)
        {
            // LE MESSAGE EST PARTI, SEUL SON IDENTIFIANT MANQUE. Echouer ici
            // ferait renvoyer un courriel deja remis.
            return null;
        }
    }

    private static string? Lire(string corps)
    {
        try
        {
            using var document = JsonDocument.Parse(corps);
            var racine = document.RootElement;

            var nom = racine.TryGetProperty("name", out var n) ? n.GetString() : null;
            var texte = racine.TryGetProperty("message", out var m) ? m.GetString() : null;

            return (nom, texte) switch
            {
                (null, null) => null,
                (null, _) => texte,
                (_, null) => nom,
                _ => $"{nom} — {texte}",
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Le corps de la requete.
    /// </summary>
    ///
    /// <remarks>
    /// LES NOMS JSON SONT ECRITS A LA MAIN parce que Resend attend
    /// « reply_to » en serpent, la ou JsonSerializerDefaults.Web produirait
    /// « replyTo ». Une adresse de reponse silencieusement ignoree, c'est un
    /// client dont la reponse se perd.
    /// </remarks>
    private sealed class DemandeResend
    {
        [JsonPropertyName("from")]
        public string From { get; init; } = string.Empty;

        [JsonPropertyName("to")]
        public IReadOnlyList<string> To { get; init; } = [];

        [JsonPropertyName("subject")]
        public string Subject { get; init; } = string.Empty;

        [JsonPropertyName("text")]
        public string Text { get; init; } = string.Empty;

        [JsonPropertyName("reply_to")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? ReplyTo { get; init; }
    }
}
