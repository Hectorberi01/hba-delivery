using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Minio;

namespace Hba.BuildingBlocks.Storage;

public static class ObjectStoreExtensions
{
    /// <summary>
    /// Enregistre le stockage objet du service.
    /// </summary>
    ///
    /// <param name="fichierEnv">
    /// Chemin du .env du service appelant, tel qu'il apparaît dans le dépôt.
    ///
    /// IL EST PASSÉ EN PARAMÈTRE PARCE QUE LE MESSAGE D'ARRÊT LE CITE. Un
    /// message partagé qui dirait « le .env du service » obligerait celui qui
    /// le lit à deviner lequel ; un message qui citerait toujours celui de
    /// Driver enverrait Directory corriger le mauvais fichier. Ce paramètre
    /// est la seule chose qui distingue deux appels par ailleurs identiques.
    /// </param>
    ///
    /// <remarks>
    /// ValidateDataAnnotations + ValidateOnStart : un bucket ou une clé absente
    /// fait REFUSER LE DÉMARRAGE. Sans cela, le service accepterait des pièces
    /// d'identité et les perdrait une par une, en ne laissant qu'une ligne
    /// d'avertissement dans les journaux.
    /// </remarks>
    public static IServiceCollection AddHbaObjectStore(
        this IServiceCollection services,
        IConfiguration configuration,
        string fichierEnv)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(fichierEnv);

        services.AddOptions<ObjectStoreOptions>()
            .Bind(configuration.GetSection(ObjectStoreOptions.SectionName))
            .ValidateDataAnnotations()

            // « The AccessKey field is required » EST EXACT ET INUTILE. Il dit
            // ce qui manque, jamais où le prendre — et ces deux clés ne se
            // devinent pas : elles n'existent qu'après « make garage-init »,
            // et Garage n'affiche le secret qu'une seule fois. Un message
            // d'arrêt qui n'indique pas le geste suivant fait perdre le quart
            // d'heure qu'il prétend faire gagner.
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.AccessKey)
                           && !string.IsNullOrWhiteSpace(options.SecretKey),
                "Les clés du stockage objet sont absentes. Le service refuse de "
                + "démarrer plutôt que d'accepter des fichiers et de les perdre. "
                + "Lancez « make garage-init » : il crée la clé dans Garage et "
                + "renseigne OBJECTSTORE_ACCESS_KEY et OBJECTSTORE_SECRET_KEY "
                + $"dans {fichierEnv}.")
            .ValidateOnStart();

        // Le client est sans état et coûte une poignée de connexions : un seul
        // suffit pour tout le service.
        services.AddSingleton<IMinioClient>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<ObjectStoreOptions>>().Value;

            return new MinioClient()
                .WithEndpoint(options.Endpoint)
                .WithCredentials(options.AccessKey, options.SecretKey)
                .WithRegion(options.Region)
                .WithSSL(options.UseSsl)
                .Build();
        });

        // UN SECOND CLIENT QUI NE PARLE A PERSONNE. Celui-ci ne sert qu'à
        // SIGNER : la signature d'une URL présignée est un calcul local, sans
        // appel réseau. Il peut donc pointer vers une adresse que le service
        // lui-même ne joindrait pas — et c'est exactement le but, puisque
        // l'adresse qu'il faut signer est celle du navigateur et du téléphone,
        // pas celle du réseau Docker.
        services.AddKeyedSingleton<IMinioClient>(ObjectStoreOptions.PresigningClientKey, (sp, _) =>
        {
            var options = sp.GetRequiredService<IOptions<ObjectStoreOptions>>().Value;

            return new MinioClient()
                .WithEndpoint(options.SigningEndpoint)
                .WithCredentials(options.AccessKey, options.SecretKey)
                .WithRegion(options.Region)
                .WithSSL(options.SigningUseSsl)
                .Build();
        });

        services.AddScoped<IObjectStore, S3ObjectStore>();

        return services;
    }
}
