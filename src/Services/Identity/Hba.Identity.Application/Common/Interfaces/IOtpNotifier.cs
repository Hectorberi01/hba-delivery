namespace Hba.Identity.Application.Common.Interfaces;

/// <summary>
/// Envoi du code de connexion. Identity ne parle à aucun opérateur : il dépose
/// une commande dans son Outbox, à destination du service Notification.
/// </summary>
public interface IOtpNotifier
{
    /// <param name="whatsAppAllowed">
    /// Le titulaire a-t-il consenti à recevoir des messages WhatsApp ? Identity
    /// ne décide pas du canal — c'est le catalogue de Notification qui le fait —
    /// mais lui seul connaît le consentement, et Meta l'exige avant tout message
    /// de gabarit. Faux ouvre le SMS seul ; vrai ouvre la chaîne WhatsApp puis
    /// SMS en repli.
    /// </param>
    void SendOtp(string phone, string code, TimeSpan validity, bool whatsAppAllowed);
}
