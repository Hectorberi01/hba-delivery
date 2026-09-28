using Hba.BuildingBlocks.Application.Abstractions;
using Hba.BuildingBlocks.Persistence;
using Hba.BuildingBlocks.Storage;
using Hba.Media.Application.Assets;
using Hba.Media.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Hba.Media.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddMediaInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddDbContext<MediaDbContext>(options =>
            options.UseNpgsql(
                configuration.Obligatoire("MediaDb"),
                npgsql => npgsql.MigrationsHistoryTable("__ef_migrations", MediaDbContext.Schema)));

        services.AddHbaAutoMigration<MediaDbContext>();
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<MediaDbContext>());
        services.AddScoped<IMediaRepository, MediaRepository>();

        // LE SEUL SERVICE QUI PORTE LA CLE DU STOCKAGE, depuis le point 27.
        // Auparavant c'etait Driver ; demain aucun autre ne l'aura, ce qui
        // supprime la question de savoir si Directory peut lire les pieces
        // d'identite d'un livreur : il n'a pas la cle.
        services.AddHbaObjectStore(configuration, "src/Services/Media/.env");

        return services;
    }
}
