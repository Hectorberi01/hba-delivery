using Hba.BuildingBlocks.Application.Abstractions;
using Hba.BuildingBlocks.Domain;
using Hba.BuildingBlocks.Application.Messaging;
using Hba.BuildingBlocks.Security;
using Hba.Driver.Application.Common.Interfaces;
using Hba.Driver.Application.Common.Views;

namespace Hba.Driver.Application.Features.Drivers.Queries;

/// <summary>
/// « Qui sont CES livreurs ? » — nom, vehicule, etats, par identifiants.
/// </summary>
///
/// <remarks>
/// POUR LA CARTE DE L'EXPLOITATION, qui lit les positions chez Location puis vient
/// nommer les livreurs ici. C'est le remplacement de ListDriverPositions, qui croisait
/// les deux sources dans ce service jusqu'au 1er octobre 2026.
///
/// ELLE NE FILTRE RIEN, ET C'EST CE QUI LA DISTINGUE DE
/// <see cref="FilterAvailableDriversHandler"/>. La carte montre aussi les livreurs en
/// course, reserves ou hors ligne : passer par le filtre d'eligibilite les ferait
/// disparaitre de l'ecran au moment ou l'exploitation en a le plus besoin — quand
/// quelqu'un ne repond plus.
///
/// RESERVEE AU BACK-OFFICE, VERIFIE ICI (ADR 0007). Elle rend des NOMS, et un nom
/// rattache a un point dit ou une personne nommee se tient. Un jeton de service, qui
/// ne porte aucun role, est refuse par la meme ligne : le dispatch n'a besoin ni des
/// noms ni des etats detailles, il lui suffit de savoir qui est offrable.
/// </remarks>
public sealed record GetDriverSummariesQuery(IReadOnlyCollection<Guid> DriverIds)
    : IQuery<IReadOnlyList<DriverSummaryView>>;

public sealed class GetDriverSummariesHandler(
    IDriverRepository drivers,
    ICallerContext caller) : IQueryHandler<GetDriverSummariesQuery, IReadOnlyList<DriverSummaryView>>
{
    public async Task<IReadOnlyList<DriverSummaryView>> HandleAsync(
        GetDriverSummariesQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (!caller.Roles.Overlaps(HbaRoles.BackOffice))
        {
            throw new ForbiddenException("L'annuaire des livreurs est reserve au back-office.");
        }

        if (query.DriverIds.Count == 0)
        {
            return [];
        }

        var profils = await drivers
            .FindByIdsAsync([.. query.DriverIds], cancellationToken)
            .ConfigureAwait(false);

        // UN IDENTIFIANT INCONNU EST SIMPLEMENT ABSENT DE LA REPONSE, sans erreur. Les
        // identifiants viennent d'un index Redis qui peut garder la trace d'un compte
        // supprime en base ; faire echouer la carte entiere pour un point orphelin
        // serait une panne la ou il suffit de ne pas le dessiner.
        return
        [
            .. profils.Select(d => new DriverSummaryView(
                d.Id,
                d.DisplayName,
                d.VerificationStatus,
                d.OperationalStatus,
                d.Vehicle.Type)),
        ];
    }
}
