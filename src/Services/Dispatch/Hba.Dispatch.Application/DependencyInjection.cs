using System.Reflection;
using Hba.BuildingBlocks.Application.Extensions;
using Hba.Dispatch.Application.Common.IntegrationEvents;
using Microsoft.Extensions.DependencyInjection;

namespace Hba.Dispatch.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddDispatchApplication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddHbaApplication(Assembly.GetExecutingAssembly());
        services.AddSingleton<IDispatchIntegrationEventPublisher, DispatchIntegrationEventPublisher>();

        return services;
    }
}
