using System.Net;
using System.Net.Mail;
using Hba.Notification.Application.Ports;
using Hba.Notification.Domain.Messages;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Hba.Notification.Infrastructure.Senders;

/// <summary>
/// Réglages SMTP.
/// </summary>
///
/// <remarks>
/// SMTP PLUTOT QU'UNE API, ET C'EST UN CHOIX ASSUME. Brevo, Mailgun, SendGrid,
/// OVH et Google Workspace offrent tous un relais SMTP : le fournisseur tient
/// alors dans trois lignes de configuration au lieu d'un adaptateur par
/// prestataire. Aucun n'ayant été choisi, c'est la seule forme qui n'engage
/// rien.
///
/// CE QU'ON PERD, ET QU'IL FAUT SAVOIR : ni accusé de remise, ni rebond, ni
/// plainte. On saura que le serveur a ACCEPTE le message, pas que le client
/// l'a reçu. Pour un reçu que personne n'attend dans la seconde, c'est
/// tenable ; pour un code de connexion, ça ne le serait pas — et c'est
/// précisément pourquoi le courriel ne porte aucun code dans ce système.
/// </remarks>
public sealed class SmtpOptions
{
    public const string SectionName = "Smtp";

    public string Host { get; set; } = string.Empty;

    /// <summary>
    /// 587 avec STARTTLS est le port de soumission standard.
    /// </summary>
    ///
    /// <remarks>
    /// LE 25 N'EST PAS UN PORT DE SOUMISSION et il est bloqué en sortie par la
    /// plupart des hébergeurs ; le 465 parle TLS dès la connexion, ce que
    /// SmtpClient ne sait pas faire. Un envoi qui « ne part pas sans erreur »
    /// vient neuf fois sur dix de ce chiffre-là.
    /// </remarks>
    public int Port { get; set; } = 587;

    public string User { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    /// <summary>Adresse d'expédition. Doit appartenir au domaine authentifié.</summary>
    public string From { get; set; } = string.Empty;

    /// <summary>Nom affiché à côté de l'adresse.</summary>
    public string FromName { get; set; } = "HBA Delivery";

    /// <summary>
    /// Adresse de réponse, quand elle diffère de l'expéditeur.
    /// </summary>
    ///
    /// <remarks>
    /// LE MODELE DU REÇU INVITE LE CLIENT A REPONDRE. Si l'expéditeur est une
    /// boîte technique que personne ne relève, sa réponse tombe dans le vide —
    /// et il conclut que HBA ne répond pas. Renseignez ici l'adresse du
    /// support, ou retirez cette phrase du modèle.
    /// </remarks>
    public string ReplyTo { get; set; } = string.Empty;

    public int TimeoutSeconds { get; set; } = 20;

    /// <summary>Vrai quand il y a de quoi se connecter ET de quoi expédier.</summary>
    public bool EstConfigure =>
        !string.IsNullOrWhiteSpace(Host) && !string.IsNullOrWhiteSpace(From);
}

/// <summary>
/// Remet un courriel à un relais SMTP.
///
/// LE CORPS EST DU TEXTE BRUT, PAS DU HTML. Un reçu de course tient en huit
/// lignes ; en faire une page HTML ajouterait une mise en page à tester dans
/// vingt clients de messagerie, et ferait basculer le message dans les
/// indésirables plus souvent qu'autrement. Le jour où il portera un logo, ce
/// sera une décision, pas un glissement.
/// </summary>
public sealed class SmtpEmailSender(
    IOptions<SmtpOptions> options,
    ILogger<SmtpEmailSender> journal) : INotificationSender
{
    private readonly SmtpOptions _options = options.Value;

    public NotificationChannel Channel => NotificationChannel.Email;

    public bool IsConfigured => _options.EstConfigure;

    public async Task<SendResult> SendAsync(
        string recipient,
        OutboundMessage message,
        CancellationToken cancellationToken)
    {
        if (message is not EmailMessage courriel)
        {
            // LE CANAL ET LE MESSAGE SE SONT DESACCORDES, et c'est un bug du
            // rendu, pas une panne du relais. Le dire ainsi evite de chercher
            // du cote du serveur pendant une heure.
            return SendResult.Failed("smtp", $"Message de type {message?.GetType().Name} sur le canal courriel.");
        }

        using var client = new SmtpClient(_options.Host, _options.Port)
        {
            EnableSsl = true,
            Timeout = _options.TimeoutSeconds * 1000,
        };

        // UN RELAIS SANS IDENTIFIANTS EXISTE : un serveur interne qui accepte
        // le domaine sur sa seule adresse IP. Poser des identifiants vides le
        // ferait echouer en « 535 authentication failed » alors qu'il ne
        // demandait rien.
        if (!string.IsNullOrWhiteSpace(_options.User))
        {
            client.Credentials = new NetworkCredential(_options.User, _options.Password);
        }

        using var mail = new MailMessage
        {
            From = new MailAddress(_options.From, _options.FromName),
            Subject = courriel.Subject,
            Body = courriel.Body,
            IsBodyHtml = false,
        };

        mail.To.Add(recipient);

        if (!string.IsNullOrWhiteSpace(_options.ReplyTo))
        {
            mail.ReplyToList.Add(new MailAddress(_options.ReplyTo));
        }

        try
        {
            await client.SendMailAsync(mail, cancellationToken).ConfigureAwait(false);

            // AUCUN IDENTIFIANT DE MESSAGE : SMTP n'en rend pas. Le champ reste
            // nul plutot que de porter un identifiant fabrique ici, qui ne
            // permettrait de retrouver ce message chez aucun fournisseur.
            return SendResult.Ok("smtp");
        }
        catch (SmtpException erreur)
        {
            journal.LogWarning(
                erreur,
                "Le relais SMTP a refuse le message ({Statut}).",
                erreur.StatusCode);

            return SendResult.Failed("smtp", $"{erreur.StatusCode} : {erreur.Message}");
        }
        catch (InvalidOperationException erreur)
        {
            // Adresse d'expedition ou hote mal formes : configuration, pas
            // reseau. Le message ne partira pas davantage au prochain essai.
            return SendResult.Failed("smtp", $"Configuration SMTP invalide : {erreur.Message}");
        }
    }
}
