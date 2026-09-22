using Hba.BuildingBlocks.Messaging.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Hba.Pricing.Infrastructure.Extensions;

public static class PricingInfrastructureExtensions
{
    /// <summary>
    /// Zones PostGIS, grilles tarifaires, devis. Seule autorite sur le prix.
    /// La persistance n'est pas encore posee : seule la messagerie commune l'est,
    /// pour que le service demarre et publie son Outbox des qu'il aura un modele.
    /// </summary>
    public static IServiceCollection AddPricingInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddHbaMessaging(configuration, producerName: "pricing", consumerGroupId: "pricing");

        return services;
    }
}
