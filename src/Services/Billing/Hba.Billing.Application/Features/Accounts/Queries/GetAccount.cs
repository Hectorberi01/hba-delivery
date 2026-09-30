using Hba.BuildingBlocks.Application.Abstractions;
using Hba.BuildingBlocks.Application.Messaging;
using Hba.BuildingBlocks.Domain;
using Hba.Billing.Application.Authorization;
using Hba.Billing.Application.Common.Interfaces;
using Hba.Billing.Application.Common.Views;

namespace Hba.Billing.Application.Features.Accounts.Queries;

/// <summary>
/// L'état d'un compte. SANS VERROU : une lecture ne décide de rien, et
/// verrouiller pour afficher un solde ferait attendre les courses derrière un
/// écran de consultation.
/// </summary>
public sealed record GetAccountQuery(string OwnerType, string OwnerId) : IQuery<BillingAccountView>;

public sealed class GetAccountHandler(IBillingAccountRepository comptes, ICallerContext caller)
    : IQueryHandler<GetAccountQuery, BillingAccountView>
{
    public async Task<BillingAccountView> HandleAsync(
        GetAccountQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        // AVANT LA LECTURE, PAS APRÈS. Vérifier une fois le compte chargé
        // marcherait aussi, mais ferait dépendre le refus de l'existence du
        // compte : « interdit » pour un compte présent, « introuvable » pour un
        // absent, et l'énumération redevient possible.
        BillingAccess.EnsureCanRead(caller, query.OwnerType, query.OwnerId);

        var compte = await comptes
            .FindByOwnerAsync(query.OwnerType, query.OwnerId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new NotFoundException("Compte de facturation", $"{query.OwnerType}:{query.OwnerId}");

        return BillingAccountView.From(compte);
    }
}
