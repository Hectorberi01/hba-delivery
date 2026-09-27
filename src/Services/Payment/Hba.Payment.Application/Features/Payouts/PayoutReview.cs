using Hba.BuildingBlocks.Application.Abstractions;
using Hba.BuildingBlocks.Domain;
using Hba.BuildingBlocks.Security;
using Hba.Payment.Application.Common.Interfaces;
using Hba.Payment.Domain.Payouts;

namespace Hba.Payment.Application.Features.Payouts;

/// <summary>
/// Ce qui est commun aux trois gestes de la finance sur une demande de
/// versement : qui a le droit de les faire, et comment la demande s'affiche
/// une fois le geste passe.
///
/// UN SEUL ENDROIT POUR LA REGLE D'ACCES, ET C'EST LE POINT DE CE FICHIER.
/// Approuver, refuser et consigner un virement sont trois commandes, donc
/// trois fichiers ; recopier le controle de role dans chacun garantit qu'un
/// jour l'un des trois divergera des deux autres — et ce sera celui par
/// lequel on paiera quelqu'un qui n'aurait pas du l'etre.
/// </summary>
internal static class PayoutReview
{
    /// <summary>
    /// FINANCE ET ADMIN, PAS TOUT LE BACK-OFFICE. Le referentiel confie a
    /// `finance` « les paiements et les reversements » : instruire une demande
    /// de versement est exactement cela. `ops` supervise des courses et des
    /// livreurs, `support` repond aux clients ; ni l'un ni l'autre n'engage
    /// l'argent de la maison. `admin` passe parce qu'il passe partout.
    ///
    /// VERIFIE ICI, DANS LE SERVICE, comme l'exige le referentiel — la
    /// passerelle ouvre son groupe a quatre roles, et ce n'est pas elle qui
    /// decide.
    /// </summary>
    internal static void EnsureReviewer(ICallerContext caller)
    {
        ArgumentNullException.ThrowIfNull(caller);

        if (!caller.IsInRole(HbaRoles.Finance) && !caller.IsInRole(HbaRoles.Admin))
        {
            throw new ForbiddenException("Seuls finance et admin instruisent une demande de versement.");
        }
    }

    /// <summary>
    /// Qui signe la decision. LE SUJET DU JETON, PAS UN NOM : un nom
    /// d'affichage change, se corrige, se traduit ; l'identifiant du compte
    /// reste celui par lequel on retrouvera la personne dans six mois.
    /// </summary>
    internal static string Signature(ICallerContext caller)
    {
        ArgumentNullException.ThrowIfNull(caller);

        return string.IsNullOrWhiteSpace(caller.SubjectId)
            ? throw new ForbiddenException("Le jeton ne porte aucun sujet : la decision ne pourrait pas etre imputee.")
            : caller.SubjectId;
    }

    internal static PayoutRequestView ToView(PayoutRequest demande)
    {
        ArgumentNullException.ThrowIfNull(demande);

        return new PayoutRequestView(
            demande.Id,
            demande.DriverId,
            demande.AmountXof,
            demande.Status,
            demande.RequestedAt,
            demande.DecidedAt,
            demande.DecidedBy,
            demande.RejectionReason,
            demande.PaidAt,
            demande.PaymentReference);
    }
}
