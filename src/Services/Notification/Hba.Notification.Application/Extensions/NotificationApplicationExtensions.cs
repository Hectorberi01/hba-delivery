using System.Reflection;
using Hba.BuildingBlocks.Application.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace Hba.Notification.Application.Extensions;

public static class NotificationApplicationExtensions
{
    public static IServiceCollection AddNotificationApplication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddHbaApplication(Assembly.GetExecutingAssembly());

        return services;
    }
}
