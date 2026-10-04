using System.Net.Sockets;
using Hba.Routing.Application.Common;
using Hba.Routing.Infrastructure.Options;
using Hba.Routing.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Hba.Routing.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddRoutingInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // LA VALIDATION EST ECRITE A LA MAIN, comme ailleurs dans ce depot. Le
        // message compte autant que la regle : « Routing:TimeoutSeconds doit
        // etre entre 1 et 30 » se corrige tout seul, la ou « DataAnnotation
        // validation failed » envoie lire du code.
        services.AddOptions<RoutingOptions>()
            .Bind(configuration.GetSection(RoutingOptions.SectionName))
            .Validate(
                o => !string.IsNullOrWhiteSpace(o.Provider),
                "Routing:Provider est obligatoire : « osrm » ou « haversine ».")
            .Validate(
                o => o.TimeoutSeconds is >= 1 and <= 30,
                "Routing:TimeoutSeconds doit etre entre 1 et 30 : un client attend.")
            .Validate(
                o => o.CacheTtlSeconds is >= 0 and <= 86_400,
                "Routing:CacheTtlSeconds doit etre entre 0 et 86400. Zero desactive le cache.")
            .ValidateOnStart();

        // LE REPLI EST TOUJOURS ENREGISTRE, quel que soit le fournisseur choisi :
        // c'est le filet, pas une variante.
        services.AddSingleton<IFallbackRouteProvider, HaversineRouteProvider>();

        var choisi = configuration[$"{RoutingOptions.SectionName}:Provider"] ?? "haversine";

        if (string.Equals(choisi, "osrm", StringComparison.OrdinalIgnoreCase))
        {
            services.AddHttpClient<IRouteProvider, OsrmRouteProvider>((sp, client) =>
            {
                var reglages = sp.GetRequiredService<IOptions<RoutingOptions>>().Value;
                client.Timeout = TimeSpan.FromSeconds(reglages.TimeoutSeconds);
            });
        }
        else
        {
            // LE MEME OBJET SERT DE FOURNISSEUR ET DE REPLI. Ce n'est pas une
            // curiosite : en developpement, tant que la carte du Benin n'est pas
            // preparee, la mesure a vol d'oiseau EST le service. Chaque reponse
            // porte alors FromFallback, ce qui est exactement la verite.
            services.AddSingleton<IRouteProvider>(
                sp => sp.GetRequiredService<IFallbackRouteProvider>());
        }

        var redis = configuration.GetConnectionString("Redis");

        if (string.IsNullOrWhiteSpace(redis))
        {
            // SANS REDIS, LE SERVICE MARCHE QUAND MEME, plus lentement. Exiger
            // un cache pour demarrer ferait d'une optimisation une dependance.
            services.AddSingleton<IRouteCache, SansCache>();

            return services;
        }

        // LE CACHE SE CONSTRUIT ICI, ET IL NE PEUT PLUS FAIRE ECHOUER UN APPEL.
        //
        // CE QUE CETTE FABRIQUE REPARE. Elle enregistrait « IConnectionMultiplexer »
        // puis « RedisRouteCache », et c'est le conteneur qui appelait Connect a
        // la premiere resolution — donc AU MILIEU D'UNE REQUETE. Le 2 octobre
        // 2026, une chaine sans mot de passe a fait lever NOAUTH a cet endroit :
        // l'exception a traverse le Dispatcher, le gestionnaire gRPC n'a jamais
        // tourne, et CalculateRoute a rendu Internal pendant des heures. Le
        // commentaire ci-dessus promettait pourtant qu'un cache absent ne coutait
        // que de la vitesse ; il ne couvrait que la chaine VIDE, jamais la chaine
        // FAUSSE, qui est le cas qui arrive vraiment.
        //
        // TROIS VERROUS, PAS UN : la connexion ne s'interrompt plus a l'echec
        // (AbortOnConnectFail), une chaine illisible retombe sur SansCache, et
        // toute panne ulterieure est avalee par CacheTolerantAuxPannes. Le
        // premier laisse demarrer, le deuxieme laisse construire, le troisieme
        // laisse servir.
        services.AddSingleton<IRouteCache>(sp =>
        {
            var fabrique = sp.GetRequiredService<ILoggerFactory>();
            var multiplexeur = Connecter(redis, fabrique.CreateLogger(typeof(DependencyInjection)));

            if (multiplexeur is null)
            {
                return new SansCache();
            }

            return new CacheTolerantAuxPannes(
                new RedisRouteCache(multiplexeur, sp.GetRequiredService<IOptions<RoutingOptions>>()),
                fabrique.CreateLogger<CacheTolerantAuxPannes>());
        });

        return services;
    }

    /// <summary>
    /// Ouvre la connexion a Redis sans jamais lever.
    /// </summary>
    ///
    /// <remarks>
    /// ABORTONCONNECTFAIL A FALSE EST LE COEUR DE LA METHODE. Par defaut,
    /// StackExchange.Redis LEVE quand le premier essai echoue — serveur
    /// endormi, mot de passe absent, nom inconnu — et ne retente jamais. A
    /// false, il rend un multiplexeur qui se reconnecte tout seul en arriere
    /// plan : le service demarre, sert sans cache, et recommence a s'en servir
    /// des que Redis revient, sans redemarrage.
    ///
    /// LE « NULL » N'EST PAS UN ECHEC SILENCIEUX : il journalise en Error, parce
    /// qu'une chaine de connexion illisible est une faute de configuration a
    /// corriger, pas un mode de fonctionnement.
    /// </remarks>
    private static IConnectionMultiplexer? Connecter(string chaine, ILogger journal)
    {
        try
        {
            var reglages = ConfigurationOptions.Parse(chaine);
            reglages.AbortOnConnectFail = false;

            return ConnectionMultiplexer.Connect(reglages);
        }
        catch (Exception erreur) when (
            erreur is RedisException
                or ArgumentException
                or FormatException
                or SocketException
                or TimeoutException)
        {
            journal.LogError(
                erreur,
                "Chaine de connexion Redis inutilisable : le service tourne SANS cache "
                + "d'itineraires. Les itineraires restent justes, le calcul est refait "
                + "a chaque appel.");

            return null;
        }
    }
}

/// <summary>Cache absent : on calcule a chaque fois.</summary>
internal sealed class SansCache : IRouteCache
{
    public Task<Hba.Routing.Domain.Routes.Route?> GetAsync(
        Hba.Routing.Domain.Routes.GeoPoint origin,
        Hba.Routing.Domain.Routes.GeoPoint destination,
        Hba.Routing.Domain.Routes.RouteProfile profile,
        bool withPolyline,
        CancellationToken cancellationToken) => Task.FromResult<Hba.Routing.Domain.Routes.Route?>(null);

    public Task SetAsync(
        Hba.Routing.Domain.Routes.GeoPoint origin,
        Hba.Routing.Domain.Routes.GeoPoint destination,
        Hba.Routing.Domain.Routes.RouteProfile profile,
        Hba.Routing.Domain.Routes.Route route,
        CancellationToken cancellationToken) => Task.CompletedTask;
}
