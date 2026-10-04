using Hba.BuildingBlocks.Application.Abstractions;
using Hba.BuildingBlocks.Application.Messaging;
using Hba.Routing.Application.Common;
using Hba.Routing.Domain.Routes;
using Microsoft.Extensions.Logging;

namespace Hba.Routing.Application.Features.Routes;

public sealed record CalculateRouteQuery(
    double OriginLatitude,
    double OriginLongitude,
    double DestinationLatitude,
    double DestinationLongitude,
    RouteProfile Profile,
    bool IncludePolyline) : IQuery<Route>;

/// <summary>
/// Calcule un trajet : cache, puis fournisseur, puis repli.
/// </summary>
///
/// <remarks>
/// L'ORDRE DES TROIS N'EST PAS NÉGOCIABLE, et chacun mérite sa phrase.
///
/// LE CACHE D'ABORD, parce qu'un itinéraire entre deux points ne change pas
/// d'une minute à l'autre et que le même trajet est redemandé sans cesse — un
/// client qui hésite sur son devis, trois vagues de dispatch depuis le même
/// point d'enlèvement.
///
/// LE FOURNISSEUR ENSUITE, et son échec n'est pas une exception rare : un moteur
/// de routage distant tombe, expire, ou n'a simplement pas de carte chargée.
/// C'est le cas NORMAL au premier démarrage, tant que le fichier OSRM du Bénin
/// n'a pas été préparé.
///
/// LE REPLI EN DERNIER, ET IL NE MENT PAS. Il rend une mesure à vol d'oiseau,
/// marquée <c>FromFallback</c>, et elle n'est JAMAIS mise en cache : une panne
/// d'une minute ne doit pas laisser des mesures fausses pendant une heure.
///
/// POURQUOI NE PAS ÉCHOUER PLUTÔT QUE REPLIER. Parce que l'appelant, lui, ne
/// peut rien faire de l'échec : un client devant son écran de commande ne
/// comprendra pas « moteur de routage indisponible ». Une mesure approchée et
/// ANNONCÉE COMME TELLE lui permet de commander ; c'est à Pricing de décider
/// s'il accepte de figer un prix sur cette base.
/// </remarks>
public sealed class CalculateRouteHandler(
    IRouteProvider provider,
    IFallbackRouteProvider fallback,
    IRouteCache cache,
    ILogger<CalculateRouteHandler> logger) : IQueryHandler<CalculateRouteQuery, Route>
{
    public async Task<Route> HandleAsync(
        CalculateRouteQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var origine = GeoPoint.Create(query.OriginLatitude, query.OriginLongitude);
        var destination = GeoPoint.Create(query.DestinationLatitude, query.DestinationLongitude);

        var connue = await cache
            .GetAsync(origine, destination, query.Profile, query.IncludePolyline, cancellationToken)
            .ConfigureAwait(false);

        if (connue is not null)
        {
            return connue;
        }

        try
        {
            var trajet = await provider
                .CalculateAsync(origine, destination, query.Profile, query.IncludePolyline, cancellationToken)
                .ConfigureAwait(false);

            await cache
                .SetAsync(origine, destination, query.Profile, trajet, cancellationToken)
                .ConfigureAwait(false);

            return trajet;
        }
        catch (OperationCanceledException)
        {
            // L'APPELANT A RENONCÉ : ce n'est pas une panne du fournisseur, et
            // lui rendre un repli reviendrait à travailler pour personne.
            throw;
        }
        catch (Exception erreur)
        {
            // ON JOURNALISE EN AVERTISSEMENT, PAS EN ERREUR, et la nuance
            // compte : le service REND une réponse. Une erreur réveillerait
            // quelqu'un la nuit pour une dégradation que le drapeau
            // FromFallback annonce déjà à l'appelant.
            logger.LogWarning(
                erreur,
                "Routage indisponible chez {Fournisseur} : repli a vol d'oiseau.",
                provider.Name);

            return await fallback
                .CalculateAsync(origine, destination, query.Profile, query.IncludePolyline, cancellationToken)
                .ConfigureAwait(false);
        }
    }
}
