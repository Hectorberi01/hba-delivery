using Hba.BuildingBlocks.Application.Abstractions;
using Hba.BuildingBlocks.Application.Messaging;
using Hba.Identity.Application.Common.Views;
using Hba.Identity.Application.Features.Accounts.Commands;
using Hba.Identity.Domain.Interfaces;

namespace Hba.Identity.Application.Features.Accounts.Queries;

/// <summary>
/// La liste des comptes du back-office : qui administre la plateforme, avec
/// quels rôles, et depuis quand.
/// </summary>
///
/// <remarks>
/// POURQUOI UNE REQUETE A PART ET NON UN FILTRE SUR UN ANNUAIRE GENERAL. Un
/// annuaire de tous les comptes serait une porte unique vers les clients, les
/// livreurs et les commerçants, chacun ayant ses propres règles d'accès. Cette
/// requête ne sait répondre que pour le back-office, et c'est ce qui la rend
/// sûre.
/// </remarks>
public sealed record ListBackOfficeAccountsQuery(int Limit) : IQuery<IReadOnlyList<AccountView>>;

public sealed class ListBackOfficeAccountsHandler(
    IAccountRepository accounts,
    ICallerContext caller) : IQueryHandler<ListBackOfficeAccountsQuery, IReadOnlyList<AccountView>>
{
    /// <summary>Zéro vaut 200 ; au-delà de 500, ramené à 500.</summary>
    private const int ParDefaut = 200;
    private const int Plafond = 500;

    public async Task<IReadOnlyList<AccountView>> HandleAsync(
        ListBackOfficeAccountsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        // admin SEULEMENT, ET LA MEME REGLE QUE POUR LA CREATION. Ops, support
        // et finance travaillent avec leurs collègues sans avoir besoin de la
        // liste de leurs rôles et de leurs dates de dernière connexion.
        AccountGuard.EnsureAdmin(caller);

        var plafond = query.Limit <= 0 ? ParDefaut : Math.Min(query.Limit, Plafond);

        var comptes = await accounts.ListBackOfficeAsync(plafond, cancellationToken).ConfigureAwait(false);

        // LA VUE NE PORTE PAS L'EMPREINTE DU MOT DE PASSE — AccountView n'expose
        // que « HasPassword ». Rien à retirer ici, et c'est voulu : le filtrage
        // des secrets appartient à la vue, pas à chaque appelant.
        return [.. comptes.Select(AccountViewMapper.ToView)];
    }
}
