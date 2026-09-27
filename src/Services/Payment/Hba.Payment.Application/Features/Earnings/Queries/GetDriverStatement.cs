using Hba.BuildingBlocks.Application.Abstractions;
using Hba.BuildingBlocks.Application.Messaging;
using Hba.BuildingBlocks.Domain;
using Hba.BuildingBlocks.Security;
using Hba.Payment.Application.Common.Interfaces;

namespace Hba.Payment.Application.Features.Earnings.Queries;

/// <summary>
/// Le relevé d'un livreur.
///
/// DEUX LECTEURS, ET DEUX RAISONS DIFFERENTES. Le livreur lit SON compte :
/// c'est son argent, et il n'a pas a demander la permission. Finance et admin
/// lisent CELUI D'UN AUTRE, pour instruire un versement. Ni ops ni support :
/// le referentiel confie les reversements a finance, et superviser des
/// courses n'est pas lire le compte de quelqu'un.
///
/// UN IDENTIFIANT VIDE SIGNIFIE « LE MIEN ». L'application livreur n'a pas a
/// connaitre son propre driver_id ni a le transmettre : le jeton le porte
/// deja, et le service le prend la. C'est aussi ce qui empeche un livreur de
/// lire le compte d'un collegue en changeant un parametre.
/// </summary>
public sealed record GetDriverStatementQuery(string? DriverId, int Limit) : IQuery<DriverStatementView>;

public sealed class GetDriverStatementHandler(
    IDriverStatementReader reader,
    ICallerContext caller) : IQueryHandler<GetDriverStatementQuery, DriverStatementView>
{
    /// <summary>Plafond par defaut, et plafond absolu du relevé.</summary>
    private const int DefaultLimit = 50;

    private const int MaxLimit = 200;

    public async Task<DriverStatementView> HandleAsync(
        GetDriverStatementQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var cible = Cible(query.DriverId);
        var plafond = query.Limit <= 0 ? DefaultLimit : Math.Min(query.Limit, MaxLimit);

        return await reader.ReadAsync(cible, plafond, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// De quel compte parle-t-on, et l'appelant y a-t-il droit ?
    ///
    /// L'ORDRE DES TESTS PORTE UN SENS. On regarde d'abord si c'est un
    /// livreur qui lit son propre compte, parce que c'est le cas de tous les
    /// jours ; le back-office vient ensuite.
    /// </summary>
    private string Cible(string? demande)
    {
        var moi = caller.DriverId;
        var estLivreur = caller.IsInRole(HbaRoles.Driver) && !string.IsNullOrWhiteSpace(moi);

        if (estLivreur && (string.IsNullOrWhiteSpace(demande) || string.Equals(demande, moi, StringComparison.Ordinal)))
        {
            return moi!;
        }

        // FINANCE ET ADMIN, PAS TOUT LE BACK-OFFICE. Le referentiel donne a
        // finance « paiements, remboursements, reversements » ; ops supervise
        // des courses et support repond aux clients. Ni l'un ni l'autre n'a
        // de raison de lire ce que HBA doit a quelqu'un.
        var estFinance = caller.IsInRole(HbaRoles.Finance) || caller.IsInRole(HbaRoles.Admin);

        if (estFinance)
        {
            if (string.IsNullOrWhiteSpace(demande))
            {
                throw new DomainException(
                    "MISSING_DRIVER_ID",
                    "Un relevé du back-office vise un livreur : l'identifiant est obligatoire.");
            }

            return demande;
        }

        // UN LIVREUR QUI DEMANDE LE COMPTE D'UN AUTRE TOMBE ICI, et le
        // message ne lui dit pas si ce compte existe.
        throw new ForbiddenException("Ce relevé ne vous est pas accessible.");
    }
}
