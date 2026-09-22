using System.Reflection;
using Hba.BuildingBlocks.Application.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace Hba.Pricing.Application.Extensions;

public static class PricingApplicationExtensions
{
    public static IServiceCollection AddPricingApplication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddHbaApplication(Assembly.GetExecutingAssembly());

        return services;
    }
}
