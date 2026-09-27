using System.Reflection;
using Hba.BuildingBlocks.Application.Extensions;
using Hba.Payment.Application.Common.IntegrationEvents;
using Microsoft.Extensions.DependencyInjection;

namespace Hba.Payment.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddPaymentApplication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddHbaApplication(Assembly.GetExecutingAssembly());
        services.AddSingleton<IPaymentIntegrationEventPublisher, PaymentIntegrationEventPublisher>();

        return services;
    }
}
