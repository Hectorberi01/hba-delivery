using Hba.BuildingBlocks.Application.Abstractions;
using Hba.BuildingBlocks.Application.Messaging;
using Hba.BuildingBlocks.Domain;
using Hba.BuildingBlocks.Security;
using Hba.Driver.Application.Common.Interfaces;
using Hba.Driver.Application.Common.Views;
using Hba.Driver.Domain.Drivers;

namespace Hba.Driver.Application.Features.Drivers.Queries;

/// <summary>
/// Annuaire des livreurs, pour le back-office.
///
/// CE N'EST PAS LE PROFIL REDUIT. Cette vue porte le telephone, le dossier
/// KYC et l'etat operationnel : elle ne sort que vers admin, ops, support et
/// finance. Un livreur ne lit pas l'annuaire de ses collegues, et un client
/// encore moins.
/// </summary>
public sealed record ListDriversQuery(
    string? Search,
    VerificationStatus? VerificationStatus,
    OperationalStatus? OperationalStatus,
    int PageSize,
    int Offset) : IQuery<DriverPageView>;

public sealed class ListDriversHandler(
    IDriverRepository drivers,
    ICallerContext caller) : IQueryHandler<ListDriversQuery, DriverPageView>
{
    public async Task<DriverPageView> HandleAsync(
        ListDriversQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        // VERIFIE ICI, PAS A LA PASSERELLE (ADR 0007). Un jeton de service,
        // qui ne porte aucun role, est refuse par la meme ligne : le dispatch
        // n'a aucun besoin de lire l'annuaire.
        if (!caller.Roles.Overlaps(HbaRoles.BackOffice))
        {
            throw new ForbiddenException("L'annuaire des livreurs est reserve au back-office.");
        }

        var (page, total) = await drivers
            .ListAsync(
                query.Search,
                query.VerificationStatus,
                query.OperationalStatus,
                query.PageSize,
                query.Offset,
                cancellationToken)
            .ConfigureAwait(false);

        return new DriverPageView([.. page.Select(DriverView.From)], total);
    }
}
