using Hba.Location.Application.Common;
using Hba.Location.Infrastructure.Options;
using Hba.Location.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace Hba.Location.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddLocationInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var connexion = configuration.GetConnectionString("Redis");

        // SANS REDIS, CE SERVICE N'A RIEN A DIRE, ET IL REFUSE DONC DE DEMARRER.
        //
        // La nuance avec Routing, dont le cache Redis est facultatif, n'est pas de
        // degre : la-bas Redis EVITE un calcul, ici Redis EST la donnee. Sans lui,
        // Location accepterait les battements, repondrait a /health, et rendrait une
        // carte vide — aucune vague ne trouverait jamais personne. C'est exactement la
        // panne invisible que ce depot a deja rencontree plusieurs fois : un service
        // qui a l'air sain et qui ne sert a rien.
        if (string.IsNullOrWhiteSpace(connexion))
        {
            throw new InvalidOperationException(
                "ConnectionStrings:Redis est absent. Les positions des chauffeurs y vivent, "
                + "et nulle part ailleurs : sans lui ce service n'a aucune donnee.");
        }

        services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(connexion));

        // LA VALIDATION EST ECRITE A LA MAIN, comme ailleurs dans ce depot. Le message
        // compte autant que la regle : « Location:FreshnessSeconds doit tenir entre 15
        // et 900 » se corrige tout seul, la ou « DataAnnotation validation failed »
        // envoie lire du code.
        services.AddOptions<LocationOptions>()
            .Bind(configuration.GetSection(LocationOptions.SectionName))
            .Validate(
                o => o.FreshnessSeconds is >= 15 and <= 900,
                "Location:FreshnessSeconds doit tenir entre 15 et 900 secondes. En dessous, un "
                + "chauffeur qui traverse un tunnel disparait de la carte ; au-dessus, on propose "
                + "des courses a des telephones eteints.")
            .Validate(
                o => o.DefaultListLimit is >= 1 and <= 1000,
                "Location:DefaultListLimit doit tenir entre 1 et 1000 : au-dela, une carte ne se "
                + "lit plus.")
            .Validate(
                o => o.PositionChannel.Length <= 128 && !o.PositionChannel.Any(char.IsWhiteSpace),
                "Location:PositionChannel est un nom de canal Redis : pas d'espace, 128 caracteres "
                + "au plus. Vide, le temps reel est simplement eteint — l'index continue d'etre "
                + "ecrit.")
            .ValidateOnStart();

        services.AddScoped<IPositionStore, RedisPositionStore>();

        return services;
    }
}
