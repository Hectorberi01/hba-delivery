using Hba.BuildingBlocks.Application.Abstractions;
using Hba.BuildingBlocks.Application.Messaging;
using Hba.Location.Application.Authorization;
using Hba.Location.Application.Common;
using Hba.Location.Domain.Positions;

namespace Hba.Location.Application.Features.Positions;

/// <summary>
/// « Celui-la, ou est-il ? », et sa distance a un point donne.
/// </summary>
///
/// <remarks>
/// CE N'EST PAS LA RECHERCHE DE PROXIMITE AVEC UN RAYON INFINI. Celle-la repond « les
/// N plus proches d'ici » : aucun rayon, aucune limite ne garantit qu'un chauffeur
/// nomme figure dans sa reponse, et les augmenter jusqu'a ce qu'il apparaisse
/// reviendrait a balayer la ville pour interroger une ligne.
///
/// SERT A L'AFFECTATION MANUELLE. L'exploitation designe quelqu'un ; le dispatch
/// demande ici s'il est joignable, et a Driver s'il est eligible. Les deux reponses
/// sont necessaires et aucune ne contient l'autre.
/// </remarks>
public sealed record GetPositionQuery(
    Guid DriverId,
    double? ReferenceLatitude,
    double? ReferenceLongitude) : IQuery<PositionLookupView>;

public sealed class GetPositionHandler(
    IPositionStore positions,
    ICallerContext caller) : IQueryHandler<GetPositionQuery, PositionLookupView>
{
    public async Task<PositionLookupView> HandleAsync(GetPositionQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        LocationAccess.EnsureCanLocateOne(caller);

        var position = await positions.FindAsync(query.DriverId, cancellationToken).ConfigureAwait(false);

        if (position is null)
        {
            return new PositionLookupView(null, null, positions.FreshnessSeconds);
        }

        var distance = query.ReferenceLatitude is { } lat && query.ReferenceLongitude is { } lng
            ? Distances.Metres(GeoPoint.Create(lat, lng), position.Point)
            : (int?)null;

        return new PositionLookupView(PositionView.From(position), distance, positions.FreshnessSeconds);
    }
}

/// <summary>
/// Les chauffeurs autour d'un point : LA GEOGRAPHIE SEULE.
/// </summary>
///
/// <remarks>
/// CE HANDLER NE SAIT PAS QUI EST ELIGIBLE, ET C'EST TOUTE LA FRONTIERE. Il ne
/// connait ni le vehicule, ni l'habilitation, ni le dossier, ni la mission en cours :
/// il rend les identifiants les plus proches dont le telephone a parle recemment.
/// L'appelant — Dispatch pour une vague, la passerelle pour une carte — demande
/// ensuite a Driver lesquels peuvent recevoir une offre.
///
/// LA SUR-INTERROGATION N'EST PAS ICI NON PLUS, ET C'EST DELIBERE. Comme la base
/// ecartera une partie des candidats, l'appelant doit en demander plus qu'il n'en
/// veut. Mais de combien depend de ce qu'il filtre : un dispatch de course de
/// personnes perd beaucoup plus de candidats qu'une carte client qui ne retient que
/// les disponibles. Fixer le facteur ici l'imposerait a tous, et il serait faux pour
/// chacun.
///
/// L'EXCLUSION, ELLE, EST ICI, et pour une raison exactement inverse : la limite
/// s'applique APRES. Filtrer les vagues precedentes chez l'appelant rendrait moins de
/// candidats que demande, et une vague se viderait au fil de ses tours sans que rien
/// ne le dise.
/// </remarks>
public sealed record FindNearbyDriversQuery(
    double Latitude,
    double Longitude,
    int RadiusMeters,
    int Limit,
    IReadOnlyCollection<Guid> ExcludeDriverIds) : IQuery<NearbyPageView>;

public sealed class FindNearbyDriversHandler(
    IPositionStore positions,
    ICallerContext caller) : IQueryHandler<FindNearbyDriversQuery, NearbyPageView>
{
    /// <summary>
    /// Marge demandee a Redis pour compenser les exclus.
    ///
    /// ELLE NE COMPENSE QUE LES EXCLUS, PAS LE FILTRE DE DRIVER. L'exclusion se fait
    /// apres la recherche : sans marge, une vague qui exclut trois chauffeurs en
    /// rendrait trois de moins que demande, alors que trois autres attendaient juste
    /// derriere. La marge est additive et non multiplicative — on sait exactement
    /// combien de noms sont exclus.
    /// </summary>
    private const int MargeMinimale = 10;

    public async Task<NearbyPageView> HandleAsync(
        FindNearbyDriversQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        LocationAccess.EnsureCanSearchNearby(caller);

        if (query.Limit <= 0 || query.RadiusMeters <= 0)
        {
            return new NearbyPageView([], positions.FreshnessSeconds);
        }

        var exclus = query.ExcludeDriverIds as IReadOnlySet<Guid> ?? query.ExcludeDriverIds.ToHashSet();

        var demande = query.Limit + Math.Max(exclus.Count, MargeMinimale);

        var candidats = await positions
            .SearchAsync(
                GeoPoint.Create(query.Latitude, query.Longitude),
                query.RadiusMeters,
                demande,
                cancellationToken)
            .ConfigureAwait(false);

        // L'ORDRE VIENT DE REDIS : la recherche geographique rend deja du plus proche
        // au plus lointain, et c'est cet ordre que la vague doit suivre.
        var retenus = candidats
            .Where(c => !exclus.Contains(c.Position.DriverId))
            .Take(query.Limit)
            .Select(c => new NearbyDriverView(PositionView.From(c.Position), c.DistanceMeters))
            .ToList();

        return new NearbyPageView(retenus, positions.FreshnessSeconds);
    }
}

/// <summary>
/// Toutes les positions recentes, pour la carte de l'exploitation.
/// </summary>
///
/// <remarks>
/// CE N'EST PAS LA RECHERCHE DE PROXIMITE SANS CENTRE, et l'ecart n'est pas qu'un
/// parametre manquant. La recherche part de l'index geographique et filtre ensuite la
/// fraicheur ; ici on part de la FRAICHEUR — les seuls chauffeurs qui comptent sont
/// ceux qui ont donne signe de vie — et on va chercher leurs coordonnees ensuite.
/// L'index GEO garde indefiniment la derniere position d'un telephone eteint, et le
/// balayer reviendrait a peindre sur la carte des chauffeurs rentres chez eux la
/// veille.
///
/// ELLE NE NOMME PERSONNE. Elle rend des identifiants et des points ; la passerelle
/// demande les noms a Driver. Location ne sait pas comment s'appellent les gens, et
/// c'est precisement ce qui lui permet de tenir des positions sans tenir un fichier.
///
/// RESERVEE AU BACK-OFFICE, VERIFIE DANS LE SERVICE. Elle rend TOUT LE MONDE : c'est
/// la lecture la plus sensible de ce service, et un jeton de service — qui ne porte
/// aucun role — est refuse par la meme ligne.
/// </remarks>
public sealed record ListFreshPositionsQuery(int Limit) : IQuery<PositionPageView>;

public sealed class ListFreshPositionsHandler(
    IPositionStore positions,
    ICallerContext caller) : IQueryHandler<ListFreshPositionsQuery, PositionPageView>
{
    public async Task<PositionPageView> HandleAsync(
        ListFreshPositionsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        LocationAccess.EnsureCanSeeEveryone(caller);

        var fraiches = await positions.ListFreshAsync(query.Limit, cancellationToken).ConfigureAwait(false);

        return new PositionPageView([.. fraiches.Select(PositionView.From)], positions.FreshnessSeconds);
    }
}
