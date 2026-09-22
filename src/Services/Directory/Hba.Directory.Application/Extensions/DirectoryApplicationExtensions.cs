using System.Reflection;
using Hba.BuildingBlocks.Application.Extensions;
using Hba.Directory.Application.IntegrationEvents;
using Microsoft.Extensions.DependencyInjection;

namespace Hba.Directory.Application.Extensions;

public static class DirectoryApplicationExtensions
{
    public static IServiceCollection AddDirectoryApplication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddHbaApplication(Assembly.GetExecutingAssembly());
        services.AddScoped<IDirectoryIntegrationEventPublisher, DirectoryIntegrationEventPublisher>();

        return services;
    }
}
