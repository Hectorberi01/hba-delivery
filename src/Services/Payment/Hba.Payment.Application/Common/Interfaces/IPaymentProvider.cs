using Hba.Payment.Domain.Payments;
using Hba.Payment.Domain.ValueObjects;

namespace Hba.Payment.Application.Common.Interfaces;

/// <summary>
/// Ce que le fournisseur rend quand on ouvre une transaction : sa reference, et
/// l'adresse de la page ou envoyer le payeur.
/// </summary>
///
/// <remarks>
/// L'ADRESSE EST REVENUE LE 3 OCTOBRE 2026, APRES AVOIR DISPARU LE MATIN MEME.
/// Entre les deux, cet enregistrement s'appelait ProviderPushResult et ne
/// portait que la reference : la demande devait partir directement sur le
/// telephone du payeur, sans rien a ouvrir.
///
/// POURQUOI CE RETOUR EN ARRIERE. Le chemin de la poussee directe a fini par
/// etre trouve — POST /v1/mtn_open, a la racine, pas sous transactions/ — mais
/// le fournisseur le refuse sur ce compte : 400 « Operation non autorisee »,
/// sans un seul champ en erreur, c'est-a-dire une methode de collecte qui n'est
/// pas ouverte chez lui. Aucune ecriture ne leve cela ; seule une activation
/// cote fournisseur le peut. La page hebergee, elle, encaisse depuis des mois.
///
/// CE QU'IL FAUDRA POUR REESSAYER : que MTN et Moov soient actifs sur le compte
/// marchand, ce que scripts/fedapay-sonde-poussee.sh verifie en trois essais.
/// </remarks>
public sealed record ProviderCheckout(string Reference, string RedirectUrl);

/// <summary>
/// Etat d'une transaction relu chez le fournisseur.
/// <paramref name="Raw"/> conserve le libelle d'origine pour les journaux et le
/// rapprochement : un statut que nous ne connaissons pas encore ne doit pas
/// disparaitre dans un « inconnu » sans trace.
/// </summary>
public sealed record ProviderPayment(PaymentStatus Status, string Raw, string? FailureReason);

/// <summary>
/// Fournisseur de paiement.
///
/// TOUTE LA CONNAISSANCE DE FEDAPAY TIENT DERRIERE CETTE INTERFACE. Le contrat
/// le dit explicitement : « Payment encapsule FedaPay derriere IPaymentProvider.
/// Aucun autre service ne connait le fournisseur. » Changer d'agregateur, c'est
/// ecrire une autre implementation, pas toucher au domaine.
/// </summary>
public interface IPaymentProvider
{
    /// <summary>Nom du fournisseur, pour les journaux et le rapport de demarrage.</summary>
    string Name { get; }

    /// <summary>
    /// Ouvre une transaction chez le fournisseur et rend la page ou envoyer le
    /// payeur.
    /// </summary>
    ///
    /// <remarks>
    /// LA MISSION N'EST PASSEE QUE POUR LE LIBELLE ET LES JOURNAUX. FedaPay ne
    /// sait rien de nos livraisons ni de nos courses : l'identifiant sert a
    /// retrouver la transaction depuis le tableau de bord marchand, ce qui est
    /// exactement ce dont finance a besoin le jour d'un remboursement manuel.
    ///
    /// LE NUMERO NE FAIT QUE PREREMPLIR LA PAGE. Le payeur peut le corriger, et
    /// c'est le compte qu'il designe LA qui sera debite — pas celui-ci. C'est
    /// aussi pour cela que la page couvre MTN, Moov et Celtiis sans que nous
    /// ayons a demander l'operateur : il le choisit lui-meme.
    ///
    /// CE QUE CET APPEL NE PROUVE PAS : que le paiement aura lieu. Il rend une
    /// adresse ; le payeur peut ne jamais l'ouvrir. Seul le webhook signe fait
    /// foi (ADR 0017).
    /// </remarks>
    Task<ProviderCheckout> OpenCheckoutAsync(
        Guid paymentIntentId,
        ServiceType serviceType,
        Guid referenceId,
        MoneyXof amount,
        string payerPhone,
        CancellationToken cancellationToken);

    /// <summary>
    /// Relit l'etat de la transaction A LA SOURCE.
    ///
    /// C'est volontairement une requete sortante et non une lecture du corps du
    /// webhook : le webhook dit QU'IL S'EST PASSE QUELQUE CHOSE, il ne dit pas
    /// ce qu'il faut croire. Sa signature prouve l'emetteur, pas la fraicheur
    /// de son contenu ; et le fournisseur lui-meme recommande de verifier le
    /// statut par l'API.
    /// </summary>
    Task<ProviderPayment> GetPaymentAsync(string providerReference, CancellationToken cancellationToken);
}

/// <summary>
/// Verification de l'authenticite d'un webhook. L'implementation est propre au
/// fournisseur ; le point d'entree HTTP ne connait que cette interface.
/// </summary>
public interface IWebhookVerifier
{
    /// <summary>
    /// Rend la reference de transaction portee par la notification, ou null si
    /// la signature ne tient pas, si l'horodatage est hors tolerance, ou si le
    /// corps ne nomme aucune transaction.
    /// </summary>
    string? ReadTransactionReference(string rawBody, string? signatureHeader);
}
