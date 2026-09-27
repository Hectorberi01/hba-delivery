using System.Reflection;
using Hba.BuildingBlocks.Application.Extensions;
using Hba.Identity.Application.Common.IntegrationEvents;
using Hba.Identity.Application.Common.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Hba.Identity.Application;

public static class DependencyInjection
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
