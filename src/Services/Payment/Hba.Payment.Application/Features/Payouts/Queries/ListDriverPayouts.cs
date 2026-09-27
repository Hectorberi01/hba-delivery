using Hba.BuildingBlocks.Application.Abstractions;
using Hba.BuildingBlocks.Application.Messaging;
using Hba.BuildingBlocks.Domain;
using Hba.BuildingBlocks.Security;
using Hba.Payment.Application.Common.Interfaces;

namespace Hba.Payment.Application.Features.Payouts.Queries;

/// <summary>
/// Les demandes de versement d'un livreur.
///
/// MEMES LECTEURS QUE LE RELEVE, ET POUR LES MEMES RAISONS : le livreur lit
/// les siennes, finance et admin celles d'un autre pour instruire. Ni ops ni
/// support.
/// </summary>
public sealed record ListDriverPayoutsQuery(string? DriverId, int Limit)
    : IQuery<IReadOnlyList<PayoutRequestView>>;

public sealed class ListDriverPayoutsHandler(
    IPayoutRequestRepository payouts,
    ICallerContext caller) : IQueryHandler<ListDriverPayoutsQuery, IReadOnlyList<PayoutRequestView>>
{
    private const int DefaultLimit = 25;

    private const int MaxLimit = 100;

    public async Task<IReadOnlyList<PayoutRequestView>> HandleAsync(
        ListDriverPayoutsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var cible = Cible(query.DriverId);
        var plafond = query.Limit <= 0 ? DefaultLimit : Math.Min(query.Limit, MaxLimit);

        return await payouts.ListForDriverAsync(cible, plafond, cancellationToken).ConfigureAwait(false);
    }

    private string Cible(string? demande)
    {
        var moi = caller.DriverId;
        var estLivreur = caller.IsInRole(HbaRoles.Driver) && !string.IsNullOrWhiteSpace(moi);

        if (estLivreur && (string.IsNullOrWhiteSpace(demande) || string.Equals(demande, moi, StringComparison.Ordinal)))
        {
            return moi!;
        }

        if (caller.IsInRole(HbaRoles.Finance) || caller.IsInRole(HbaRoles.Admin))
        {
            if (string.IsNullOrWhiteSpace(demande))
            {
                throw new DomainException(
                    "MISSING_DRIVER_ID",
                    "Une lecture du back-office vise un livreur : l'identifiant est obligatoire.");
            }

            return demande;
        }

        throw new ForbiddenException("Ces demandes ne vous sont pas accessibles.");
    }
}
