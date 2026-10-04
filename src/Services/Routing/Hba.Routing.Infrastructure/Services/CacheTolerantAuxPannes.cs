using Hba.Routing.Application.Common;
using Hba.Routing.Domain.Routes;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Hba.Routing.Infrastructure.Services;

/// <summary>
/// Enveloppe un cache pour qu'une panne de Redis ne coûte jamais un itinéraire.
/// </summary>
///
/// <remarks>
/// POURQUOI CETTE CLASSE EXISTE. Le 2 octobre 2026, chaque CalculateRoute a
/// échoué en Internal pendant des heures. La cause n'était pas OSRM, ni le
/// réseau, ni la carte : le mot de passe de Redis manquait dans la chaîne de
/// connexion, et StackExchange.Redis levait NOAUTH. Comme le multiplexeur était
/// résolu PENDANT la requête, l'exception remontait jusqu'au gestionnaire gRPC
/// et emportait la réponse. Pricing repliait alors sur une mesure à vol
/// d'oiseau, et tous les prix de la plateforme étaient sous-estimés sans que
/// rien ne le dise.
///
/// CE QU'UN CACHE A LE DROIT DE FAIRE QUAND IL TOMBE : ralentir. Rien d'autre.
/// Un cache est une optimisation ; dès qu'il peut faire échouer ce qu'il
/// accélère, il est devenu une dépendance, et le commentaire de
/// DependencyInjection qui annonce l'inverse devient faux. Une lecture qui
/// échoue est un défaut de cache ; une écriture qui échoue est une entrée qu'on
/// recalculera.
///
/// LE JOURNAL EST BRIDÉ A UNE LIGNE PAR MINUTE. Un Redis à terre reçoit autant
/// d'erreurs que la plateforme reçoit de devis : sans ce frein, la panne
/// noierait les journaux où on la cherche, et ferait grossir le disque plus vite
/// que les requêtes n'arrivent.
/// </remarks>
internal sealed class CacheTolerantAuxPannes(
    IRouteCache interne,
    ILogger<CacheTolerantAuxPannes> logger) : IRouteCache
{
    private static readonly TimeSpan SilenceEntreDeuxPlaintes = TimeSpan.FromMinutes(1);

    private DateTimeOffset _dernierePlainte = DateTimeOffset.MinValue;

    public async Task<Route?> GetAsync(
        GeoPoint origin,
        GeoPoint destination,
        RouteProfile profile,
        bool withPolyline,
        CancellationToken cancellationToken)
    {
        try
        {
            return await interne
                .GetAsync(origin, destination, profile, withPolyline, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception erreur) when (EstUnePanneDeCache(erreur))
        {
            Plaindre(erreur, "lecture");
            return null;
        }
    }

    public async Task SetAsync(
        GeoPoint origin,
        GeoPoint destination,
        RouteProfile profile,
        Route route,
        CancellationToken cancellationToken)
    {
        try
        {
            await interne
                .SetAsync(origin, destination, profile, route, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception erreur) when (EstUnePanneDeCache(erreur))
        {
            Plaindre(erreur, "ecriture");
        }
    }

    /// <summary>
    /// Les pannes qu'un cache a le droit d'avaler.
    /// </summary>
    ///
    /// <remarks>
    /// LA LISTE EST FERMEE, ET C'EST VOLONTAIRE. Avaler toute exception ferait
    /// disparaître les vrais défauts — une sérialisation cassée, une clé mal
    /// construite — sous le même silence qu'une panne de réseau. Ce qui n'est
    /// pas listé ici remonte, et c'est ce qu'on veut.
    ///
    /// L'ANNULATION N'EN EST PAS UNE : si l'appelant renonce, le cache n'a rien
    /// à pardonner, et avaler l'annulation ferait continuer un travail que
    /// personne n'attend plus.
    /// </remarks>
    private static bool EstUnePanneDeCache(Exception erreur) => erreur switch
    {
        OperationCanceledException => false,
        RedisException => true,
        TimeoutException => true,

        // LE MULTIPLEXEUR PEUT AVOIR ETE LIBERE sous nos pieds pendant un arrêt
        // du service : ce n'est pas un défaut, c'est la fin de la vie du
        // processus.
        ObjectDisposedException => true,
        _ => false,
    };

    private void Plaindre(Exception erreur, string geste)
    {
        var maintenant = DateTimeOffset.UtcNow;
        if (maintenant - _dernierePlainte < SilenceEntreDeuxPlaintes)
        {
            return;
        }

        _dernierePlainte = maintenant;

        logger.LogWarning(
            erreur,
            "Cache d'itineraires indisponible en {Geste} : on calcule sans lui. "
            + "Les itineraires restent justes, le service est seulement plus lent.",
            geste);
    }
}
