using Hba.BuildingBlocks.Application.Abstractions;
using Hba.BuildingBlocks.Application.Messaging;
using Hba.BuildingBlocks.Domain;
using Hba.BuildingBlocks.Security;
using Hba.Driver.Application.Common.Interfaces;
using Hba.Driver.Application.Common.Views;

namespace Hba.Driver.Application.Features.Drivers.Queries;

public sealed record GetDriverQuery(Guid DriverId) : IQuery<DriverView>;

/// <summary>
/// Profil reduit, destine au client et au commercant pendant la mission.
/// Requete distincte de <see cref="GetDriverQuery"/> a dessein : deux vues
/// differentes, deux autorisations differentes, et aucun risque de servir la
/// complete a la place de la reduite par inadvertance.
/// </summary>
public sealed record GetDriverPublicProfileQuery(Guid DriverId) : IQuery<DriverPublicProfileView>;

public sealed class GetDriverHandler(
    IDriverRepository drivers,
    ICallerContext caller) : IQueryHandler<GetDriverQuery, DriverView>
{
    public async Task<DriverView> HandleAsync(GetDriverQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var driver = await drivers.GetByIdAsync(query.DriverId, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException("Livreur", query.DriverId.ToString());

        // LE DOSSIER COMPLET NE SORT PAS VERS N'IMPORTE QUI : le livreur
        // lui-meme, ou le back-office. Un client qui veut savoir qui vient
        // chercher son colis passe par le profil reduit.
        var self = caller.DriverId ?? caller.SubjectId;
        var backOffice = caller.IsInRole(HbaRoles.Admin)
            || caller.IsInRole(HbaRoles.Ops)
            || caller.IsInRole(HbaRoles.Support);

        if (!backOffice && !string.Equals(self, query.DriverId.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            throw new ForbiddenException("Ce dossier livreur ne vous est pas accessible.");
        }

        return DriverView.From(driver);
    }
}

public sealed class GetDriverPublicProfileHandler(IDriverRepository drivers)
    : IQueryHandler<GetDriverPublicProfileQuery, DriverPublicProfileView>
{
    public async Task<DriverPublicProfileView> HandleAsync(
        GetDriverPublicProfileQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var driver = await drivers.GetByIdAsync(query.DriverId, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException("Livreur", query.DriverId.ToString());

        // AUCUN FILTRE PAR APPELANT ICI, ET C'EST UN POINT A TRANCHER. La vue
        // est deja reduite au strict necessaire — nom, telephone, vehicule —
        // mais rien ne verifie que celui qui demande a bien une course en
        // cours avec ce livreur. Le faire demanderait d'interroger Delivery
        // depuis Driver, donc une dependance que l'architecture evite. A
        // defaut, la gateway restreint la route ; c'est moins fort que l'ADR
        // 0007 ne l'exige, et cela doit etre tranche.
        return DriverPublicProfileView.From(driver);
    }
}
