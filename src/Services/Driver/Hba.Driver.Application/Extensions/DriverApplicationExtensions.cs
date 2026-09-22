using System.Reflection;
using Hba.BuildingBlocks.Application.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace Hba.Driver.Application.Extensions;

public static class DriverApplicationExtensions
{
    public static IServiceCollection AddDriverApplication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddHbaApplication(Assembly.GetExecutingAssembly());

        return services;
    }
}
