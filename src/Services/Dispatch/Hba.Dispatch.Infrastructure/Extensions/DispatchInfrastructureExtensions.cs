using Hba.BuildingBlocks.Messaging.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Hba.Dispatch.Infrastructure.Extensions;

public static class DispatchInfrastructureExtensions
{
    /// <summary>
    /// Moteur d'offres par vagues. Peu de domaine, beaucoup d'infrastructure Redis.
    /// La persistance n'est pas encore posee : seule la messagerie commune l'est,
    /// pour que le service demarre et publie son Outbox des qu'il aura un modele.
    /// </summary>
    public static IServiceCollection AddDispatchInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddHbaMessaging(configuration, producerName: "dispatch", consumerGroupId: "dispatch");

        return services;
    }
}
