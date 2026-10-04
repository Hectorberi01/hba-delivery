using Hba.BuildingBlocks.Application.Messaging;
using Hba.BuildingBlocks.Domain;
using Hba.Routing.Application.Common;
using Hba.Routing.Domain.Exceptions;
using Hba.Routing.Domain.Routes;
using Microsoft.Extensions.Logging;

namespace Hba.Routing.Application.Features.Routes;

public sealed record CalculateMatrixQuery(
    IReadOnlyList<(double Latitude, double Longitude)> Origins,
    IReadOnlyList<(double Latitude, double Longitude)> Destinations,
    RouteProfile Profile) : IQuery<RouteMatrix>;

/// <summary>
/// Calcule une matrice de trajets.
/// </summary>
///
/// <remarks>
/// PAS DE CACHE ICI, CONTRAIREMENT AU TRAJET SIMPLE, et ce n'est pas un oubli.
/// Une matrice est une combinaison d'origines qui ne se reproduit jamais à
/// l'identique : les chauffeurs se sont déplacés entre deux vagues. La clé
/// serait donc unique à chaque appel — on paierait l'écriture sans jamais
/// relire. Les trajets UNITAIRES, eux, restent cachés par l'autre chemin.
///
/// LE PLAFOND EXISTE PARCE QUE LE COÛT EST UN PRODUIT. Vingt origines et vingt
/// destinations font quatre cents calculs ; le moteur les traite en une passe,
/// mais pas gratuitement, et le client attend pendant ce temps. Quatre cents
/// cases est déjà bien au-delà de ce dont une vague de dispatch a besoin.
/// </remarks>
public sealed class CalculateMatrixHandler(
    IRouteProvider provider,
    IFallbackRouteProvider fallback,
    ILogger<CalculateMatrixHandler> logger) : IQueryHandler<CalculateMatrixQuery, RouteMatrix>
{
    /// <summary>Nombre maximal de cases d'une matrice.</summary>
    public const int PlafondDeCases = 400;

    public async Task<RouteMatrix> HandleAsync(
        CalculateMatrixQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (query.Origins.Count == 0 || query.Destinations.Count == 0)
        {
            throw new DomainException(
                RoutingErrorCodes.EmptyMatrix,
                "Une matrice demande au moins une origine et une destination.");
        }

        if (query.Origins.Count * query.Destinations.Count > PlafondDeCases)
        {
            throw new DomainException(
                RoutingErrorCodes.MatrixTooLarge,
                $"Une matrice ne depasse pas {PlafondDeCases} cases.");
        }

        var origines = query.Origins.Select(p => GeoPoint.Create(p.Latitude, p.Longitude)).ToList();
        var destinations = query.Destinations.Select(p => GeoPoint.Create(p.Latitude, p.Longitude)).ToList();

        try
        {
            return await provider
                .CalculateMatrixAsync(origines, destinations, query.Profile, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception erreur)
        {
            // LE REPLI EST PLUS GRAVE ICI QUE POUR UN TRAJET SIMPLE, et il faut
            // le dire. Une matrice sert a CLASSER des chauffeurs : a vol
            // d'oiseau, celui qui se trouve de l'autre cote de la lagune parait
            // le plus proche alors qu'il mettra quarante minutes. Le dispatch
            // qui lit FromFallback devrait donc se rabattre sur son ancien
            // comportement — le rayon Redis — plutot que de croire ce
            // classement.
            logger.LogWarning(
                erreur,
                "Matrice indisponible chez {Fournisseur} : repli a vol d'oiseau.",
                provider.Name);

            return await fallback
                .CalculateMatrixAsync(origines, destinations, query.Profile, cancellationToken)
                .ConfigureAwait(false);
        }
    }
}
