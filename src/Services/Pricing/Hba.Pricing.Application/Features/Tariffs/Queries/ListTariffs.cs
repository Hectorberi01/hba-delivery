using Hba.BuildingBlocks.Application.Abstractions;
using Hba.BuildingBlocks.Application.Messaging;
using Hba.Pricing.Application.Authorization;
using Hba.Pricing.Application.Common.Interfaces;
using Hba.Pricing.Application.Common.Views;
using Hba.Pricing.Domain.Tariffs;

namespace Hba.Pricing.Application.Features.Tariffs.Queries;

/// <summary>
/// Les grilles connues, filtrees.
/// </summary>
///
/// <remarks>
/// LIRE LES GRILLES EST AUSSI RESERVE A admin ET finance, alors que lire n'a
/// jamais change un prix. La raison n'est pas la prudence, c'est la
/// concurrence : le bareme complet d'une ville — prix au kilometre, majoration,
/// part du livreur — est une information commerciale. Un livreur y lirait
/// exactement ce que la plateforme garde sur chaque course.
///
/// C'EST PLUS STRICT QUE BillingAccess, qui ouvre la lecture a tout le
/// back-office. Le rapprochement est volontaire : un solde regarde UN compte,
/// une grille regarde TOUS les clients d'une zone a la fois.
///
/// LES TROIS FILTRES SONT FACULTATIFS ET INDEPENDANTS. Celui de la nature est
/// arrive le 1er octobre 2026 avec les grilles de course : sans lui, l'ecran
/// d'administration d'une ville melange le bareme des colis et celui des
/// courses dans une seule liste, ou deux lignes ne differaient plus que par un
/// montant.
/// </remarks>
public sealed record ListTariffsQuery(
    string? ZoneCode,
    ServiceType? ServiceType,
    VehicleType? VehicleType,
    bool IncludeClosed) : IQuery<IReadOnlyList<TariffView>>;

public sealed class ListTariffsHandler(
    ITariffRepository tariffs,
    ICallerContext caller,
    IClock clock) : IQueryHandler<ListTariffsQuery, IReadOnlyList<TariffView>>
{
    public async Task<IReadOnlyList<TariffView>> HandleAsync(
        ListTariffsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        PricingAccess.EnsureCanAdministerTariffs(caller);

        var grilles = await tariffs
            .ListAsync(
                query.ZoneCode,
                query.ServiceType,
                query.VehicleType,
                query.IncludeClosed,
                clock.UtcNow,
                cancellationToken)
            .ConfigureAwait(false);

        return [.. grilles.Select(TariffView.From)];
    }
}
