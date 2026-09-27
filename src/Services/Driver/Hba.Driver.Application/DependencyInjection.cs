using System.Reflection;
using Hba.BuildingBlocks.Application.Extensions;
using Hba.Driver.Application.Common.IntegrationEvents;
using Microsoft.Extensions.DependencyInjection;

namespace Hba.Driver.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddDriverApplication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddHbaApplication(Assembly.GetExecutingAssembly());
        services.AddSingleton<IDriverIntegrationEventPublisher, DriverIntegrationEventPublisher>();

        return services;
    }
}
