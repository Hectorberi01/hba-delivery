using System.Reflection;
using Hba.BuildingBlocks.Application.Extensions;
using Hba.Delivery.Application.IntegrationEvents;
using Microsoft.Extensions.DependencyInjection;

namespace Hba.Delivery.Application.Extensions;

public static class DeliveryApplicationExtensions
{
    public static IServiceCollection AddDeliveryApplication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddHbaApplication(Assembly.GetExecutingAssembly());
        services.AddScoped<IDeliveryIntegrationEventPublisher, DeliveryIntegrationEventPublisher>();

        return services;
    }
}
