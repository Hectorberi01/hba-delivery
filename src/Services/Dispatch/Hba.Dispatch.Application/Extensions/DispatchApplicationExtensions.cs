using System.Reflection;
using Hba.BuildingBlocks.Application.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace Hba.Dispatch.Application.Extensions;

public static class DispatchApplicationExtensions
{
    public static IServiceCollection AddDispatchApplication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddHbaApplication(Assembly.GetExecutingAssembly());

        return services;
    }
}
