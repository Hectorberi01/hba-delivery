using Hba.BuildingBlocks.Application.Messaging;
using Hba.Driver.Application.Common.Interfaces;
using Hba.Driver.Application.Common.Views;
using Hba.Driver.Domain.Drivers;

namespace Hba.Driver.Application.Features.Drivers.Queries;

/// <summary>
/// « Parmi CES livreurs, lesquels peuvent recevoir une offre ? »
/// </summary>
///
/// <remarks>
/// C'EST LA MOITIE D'UNE RECHERCHE DE PROXIMITE, ET L'AUTRE MOITIE EST CHEZ LOCATION.
/// Jusqu'au 1er octobre 2026, un seul handler faisait les deux : Redis rendait les
/// plus proches, la base ecartait les non-eligibles. Depuis que les positions ont
/// quitte ce service, Driver ne sait plus produire la liste de depart — il ne fait
/// plus que la filtrer.
///
/// AUCUNE COORDONNEE N'ENTRE NI NE SORT D'ICI, et c'est a cela qu'on reconnait que la
/// frontiere tient. Ce handler ne sait pas pourquoi ces identifiants-la lui sont
/// soumis, ni dans quel ordre les rendre : le classement par distance appartient a
/// celui qui a interroge Location.
///
/// AUCUN CONTROLE DE ROLE, comme pour <see cref="CheckDriverAvailabilityHandler"/> et
/// contrairement a <see cref="GetDriverSummariesHandler"/>. La difference n'est pas un
/// oubli : cette lecture ne rend ni nom, ni telephone — un identifiant et un type de
/// vehicule. Elle est appelee par Dispatch avec un jeton de service, et par la
/// passerelle pour la carte du client, qui en jette les identifiants (point 24).
///
/// LE FILTRE D'HABILITATION SE FAIT EN MEMOIRE, ET C'EST DELIBERE. La regle tient en
/// deux tables du domaine — ce que le vehicule permet, ce que le livreur a declare —
/// et <c>DriverAggregate.Habilite</c> les combine deja. La reecrire en SQL la ferait
/// exister en deux exemplaires, dont l'un ne serait jamais teste : c'est exactement
/// comme cela qu'un cycliste finirait par recevoir une course de personnes parce que
/// la requete, elle, aurait oublie la table. Le cout est nul — la liste ne porte que
/// quelques dizaines de candidats, et les agregats sont deja charges.
/// </remarks>
public sealed record FilterAvailableDriversQuery(
    IReadOnlyCollection<Guid> DriverIds,
    VehicleType? VehicleType,

    // NULL VAUT « LIVRAISON DE COLIS ». C'est ce que faisaient tous les appelants
    // avant que les habilitations existent, et un appelant qui oublie le champ ne doit
    // pas se voir proposer un cycliste pour une course de personnes.
    DriverCapability? RequiredCapability = null)
    : IQuery<IReadOnlyList<EligibleDriverView>>;

public sealed class FilterAvailableDriversHandler(IDriverRepository drivers)
    : IQueryHandler<FilterAvailableDriversQuery, IReadOnlyList<EligibleDriverView>>
{
    public async Task<IReadOnlyList<EligibleDriverView>> HandleAsync(
        FilterAvailableDriversQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (query.DriverIds.Count == 0)
        {
            // UNE LISTE VIDE REND UNE LISTE VIDE, PAS « TOUS LES LIVREURS ». Sans ce
            // garde-fou, FindAvailableAsync avec une liste vide pourrait, selon
            // l'implementation du depot, rendre l'annuaire entier : une vague enverrait
            // alors des offres a toute la ville parce que Location n'a trouve personne.
            return [];
        }

        var eligibles = await drivers
            .FindAvailableAsync([.. query.DriverIds], query.VehicleType, cancellationToken)
            .ConfigureAwait(false);

        var exigee = query.RequiredCapability ?? DriverCapability.PackageDelivery;

        return
        [
            .. eligibles
                .Where(d => d.Habilite(exigee))
                .Select(d => new EligibleDriverView(d.Id, d.Vehicle.Type)),
        ];
    }
}
