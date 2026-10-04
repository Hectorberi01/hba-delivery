using System.Reflection;
using Hba.BuildingBlocks.Application.Extensions;
using Hba.Ride.Application.Common.IntegrationEvents;
using Microsoft.Extensions.DependencyInjection;

namespace Hba.Ride.Application;

public static class DependencyInjection
{
    /// <summary>
    /// Inscrit les gestionnaires de commandes et de requêtes de Ride.
    /// </summary>
    ///
    /// <remarks>
    /// AUCUN PORT N'EST INSCRIT ICI, ET C'EST VOULU. <c>IRideRepository</c> et
    /// <c>IPricingClient</c> sont des interfaces : leurs implémentations vivent
    /// dans l'Infrastructure, qui seule connaît EF Core et gRPC. Les poser ici
    /// ferait dépendre l'Application de ce qu'elle cherche justement à ignorer.
    /// </remarks>
    public static IServiceCollection AddRideApplication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddHbaApplication(Assembly.GetExecutingAssembly());
        services.AddSingleton<IRideIntegrationEventPublisher, RideIntegrationEventPublisher>();

        return services;
    }
}
