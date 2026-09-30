using Hba.Delivery.Domain.ValueObjects;

namespace Hba.Delivery.Application.Ports;

/// <summary>
/// Accès au service Billing : le compte du donneur d'ordre professionnel.
/// </summary>
///
/// <remarks>
/// L'APPEL EST SYNCHRONE, ET C'EST LE POINT DE TOUT CE CHEMIN. On débite AVANT
/// de créer la course. La voie asynchrone — créer, publier, laisser Billing
/// débiter — respecterait mieux le découplage, mais un commerçant au plafond
/// recevrait une course créée suivie d'un échec que personne ne lui annonce.
///
/// LE DÉBIT ORPHELIN EST LE PRIX DE CE CHOIX, ET IL EST COMPENSÉ ICI. Si le
/// débit réussit et que la création échoue juste après, l'argent est parti et
/// rien n'est arrivé : <see cref="ReverseDebitAsync"/> le rend, appelée par le
/// gestionnaire dans son rattrapage.
///
/// CE QUI RESTE DÉCOUVERT, ET QU'IL FAUT SAVOIR : si le PROCESSUS s'arrête
/// entre le débit et la compensation — arrêt brutal, conteneur tué — personne
/// ne compense. Ce n'est pas une transaction distribuée et ça n'a pas à l'être ;
/// il faut un rapprochement périodique qui confronte les débits de Billing aux
/// courses de Delivery. CE RAPPROCHEMENT N'EXISTE PAS ENCORE, et aucun des deux
/// services ne peut le faire seul : voir points-a-trancher.
/// </remarks>
public interface IBillingClient
{
    /// <summary>
    /// Débite le compte, ou échoue. Rend l'identifiant du mouvement comptable.
    /// </summary>
    ///
    /// <remarks>
    /// LA CLE D'IDEMPOTENCE EST L'IDENTIFIANT DE LA COURSE, tiré avant l'appel :
    /// un rejeu retrouve le même mouvement au lieu d'en créer un second.
    /// </remarks>
    Task<string> DebitAsync(
        string ownerType,
        string ownerId,
        MoneyXof amount,
        string reference,
        string idempotencyKey,
        CancellationToken cancellationToken);

    /// <summary>
    /// Annule un débit dont la course n'a pas pu être créée. IDEMPOTENT.
    /// </summary>
    ///
    /// <remarks>
    /// ON DÉSIGNE LE DÉBIT PAR LA CLÉ QU'ON A DONNÉE, et non par l'identifiant
    /// du mouvement : dans le cas où tout échoue, on n'a rien pu retenir.
    ///
    /// DEUX APPELS DONNENT UN SEUL REMBOURSEMENT. Billing dérive la clé de
    /// l'annulation de celle du débit ; un second appel retrouve l'écriture
    /// déjà faite.
    ///
    /// CET APPEL PART AVEC LE JETON DE SERVICE, PAS CELUI DU DONNEUR D'ORDRE, et
    /// c'est la seule chose qui empêche la course gratuite. La clé du débit est
    /// l'identifiant de la course, que l'appelant connaît : tant que Billing
    /// acceptait l'annulation du titulaire, celui-ci pouvait se faire rembourser
    /// une course EN COURS. Seul le système sait qu'une création a échoué.
    /// </remarks>
    /// <summary>
    /// Le compte existe-t-il ? Vrai ou faux, jamais d'exception pour une absence.
    /// </summary>
    ///
    /// <remarks>
    /// POUR NE PAS BRULER LE DEVIS D'UN DONNEUR D'ORDRE QUI N'A PAS DE COMPTE.
    ///
    /// Le devis est consommé AVANT le débit — l'ordre inverse supposerait de lire
    /// un prix sans consommer le devis, ce que le contrat de Pricing ne propose
    /// pas. La première course d'un partenaire sans compte échouait donc en
    /// « compte de facturation introuvable » et lui coûtait son devis ; il en
    /// redemandait un, et le brûlait aussi, jusqu'à ce que `finance` ouvre le
    /// compte. Rien dans le message ne disait ce qu'il fallait faire.
    ///
    /// UN APPEL DE PLUS SUR LE CHEMIN B2B, ET IL EST ASSUMÉ. C'est un aller-retour
    /// vers un service qu'on va de toute façon appeler deux lignes plus bas, et il
    /// n'a lieu que pour un commerçant ou un partenaire — jamais pour un client
    /// particulier.
    ///
    /// SEULE L'ABSENCE REND FAUX. Un Billing injoignable lève, et c'est voulu :
    /// traiter une panne comme une absence de compte ferait refuser les courses de
    /// tout le monde avec un message qui accuse la finance.
    /// </remarks>
    Task<bool> AccountExistsAsync(string ownerType, string ownerId, CancellationToken cancellationToken);

    Task ReverseDebitAsync(
        string ownerType,
        string ownerId,
        string debitIdempotencyKey,
        CancellationToken cancellationToken);
}
