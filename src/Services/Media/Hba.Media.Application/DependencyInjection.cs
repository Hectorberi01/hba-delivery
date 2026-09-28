using System.Reflection;
using Hba.BuildingBlocks.Application.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace Hba.Media.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddMediaApplication(this IServiceCollection services)
        => services.AddHbaApplication(Assembly.GetExecutingAssembly());
}
