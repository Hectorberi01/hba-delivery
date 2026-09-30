using System.Reflection;
using Hba.Billing.Application.IntegrationEvents;
using Hba.BuildingBlocks.Application.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace Hba.Billing.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddBillingApplication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddHbaApplication(Assembly.GetExecutingAssembly());

        // LE PUBLIEUR EST ENREGISTRE ICI, ET LE DbContext EN DEPEND. L'ordre
        // inverse — l'Outbox injectee dans le publieur — formerait un cycle que
        // le conteneur refuse de resoudre, y compris quand « dotnet ef »
        // instancie le contexte.
        services.AddScoped<IBillingIntegrationEventPublisher, BillingIntegrationEventPublisher>();

        return services;
    }
}
