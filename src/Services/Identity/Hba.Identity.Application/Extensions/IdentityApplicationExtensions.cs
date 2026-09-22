using System.Reflection;
using Hba.BuildingBlocks.Application.Extensions;
using Hba.Identity.Application.IntegrationEvents;
using Hba.Identity.Application.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Hba.Identity.Application.Extensions;

public static class IdentityApplicationExtensions
{
    public static IServiceCollection AddIdentityApplication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddHbaApplication(Assembly.GetExecutingAssembly());
        services.AddScoped<SessionIssuer>();
        services.AddScoped<IIdentityIntegrationEventPublisher, IdentityIntegrationEventPublisher>();

        return services;
    }
}
