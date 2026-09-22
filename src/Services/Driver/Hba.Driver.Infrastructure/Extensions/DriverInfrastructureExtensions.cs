using Hba.BuildingBlocks.Messaging.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Hba.Driver.Infrastructure.Extensions;

public static class DriverInfrastructureExtensions
{
    /// <summary>
    /// Profils livreurs, KYC dans MinIO, positions dans Redis GEO.
    /// La persistance n'est pas encore posee : seule la messagerie commune l'est,
    /// pour que le service demarre et publie son Outbox des qu'il aura un modele.
    /// </summary>
    public static IServiceCollection AddDriverInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddHbaMessaging(configuration, producerName: "driver", consumerGroupId: "driver");

        return services;
    }
}
