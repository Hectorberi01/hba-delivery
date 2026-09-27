using Hba.BuildingBlocks.Application.Abstractions;
using Hba.BuildingBlocks.Application.Messaging;
using Hba.BuildingBlocks.Domain;
using Hba.BuildingBlocks.Security;
using Hba.Driver.Application.Common.Interfaces;
using Hba.Driver.Application.Common.Views;

namespace Hba.Driver.Application.Features.Drivers.Queries;

/// <summary>
/// Positions courantes des livreurs, pour la carte d'exploitation.
///
/// DEUX SOURCES, CROISEES ICI. La position vient de Redis, le nom et l'etat
/// viennent de la base : c'est la separation posee par le referentiel — « la
/// position courante ne s'ecrit jamais en base relationnelle ». Le croisement
/// se fait donc a la lecture, dans ce handler, et nulle part ailleurs.
///
/// CE N'EST PAS UNE PISTE. On rend le DERNIER point de chaque livreur, pas
/// son trajet : Redis GEO n'en garde qu'un, et c'est voulu. Une carte qui
/// afficherait un historique demanderait de stocker chaque battement, ce que
/// le referentiel exclut.
/// </summary>
public sealed record ListDriverPositionsQuery(int Limit) : IQuery<DriverPositionPageView>;

public sealed class ListDriverPositionsHandler(
    IDriverLocationStore locations,
    IDriverRepository drivers,
    ICallerContext caller) : IQueryHandler<ListDriverPositionsQuery, DriverPositionPageView>
{
    public async Task<DriverPositionPageView> HandleAsync(
        ListDriverPositionsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        // VERIFIE ICI, PAS A LA PASSERELLE (ADR 0007). Savoir ou se trouvent
        // les livreurs en ce moment est la donnee la plus sensible du service
        // apres les pieces d'identite : elle dit ou une personne se tient.
        // Un jeton de service, qui ne porte aucun role, est refuse par la
        // meme ligne — le dispatch cherche par proximite, il ne balaie pas.
        if (!caller.Roles.Overlaps(HbaRoles.BackOffice))
        {
            throw new ForbiddenException("La carte des livreurs est reservee au back-office.");
        }

        var positions = await locations
            .ListFreshAsync(query.Limit, cancellationToken)
            .ConfigureAwait(false);

        if (positions.Count == 0)
        {
            return new DriverPositionPageView([], locations.FreshnessSeconds);
        }

        var profils = await drivers
            .FindByIdsAsync([.. positions.Select(p => p.DriverId)], cancellationToken)
            .ConfigureAwait(false);

        var parId = profils.ToDictionary(d => d.Id);

        // UN POINT SANS PROFIL EST IGNORE, PAS INVENTE. Le cas ne devrait pas
        // se produire — le profil nait avant le premier battement — mais s'il
        // arrive, un marqueur « livreur inconnu » sur la carte poserait a ops
        // une question a laquelle personne ne peut repondre.
        var vues = positions
            .Where(p => parId.ContainsKey(p.DriverId))
            .Select(p =>
            {
                var driver = parId[p.DriverId];

                return new DriverPositionView(
                    driver.Id.ToString(),
                    driver.DisplayName,
                    p.Latitude,
                    p.Longitude,
                    p.SeenAt,
                    driver.OperationalStatus,
                    driver.Vehicle.Type);
            })
            .ToList();

        return new DriverPositionPageView(vues, locations.FreshnessSeconds);
    }
}
