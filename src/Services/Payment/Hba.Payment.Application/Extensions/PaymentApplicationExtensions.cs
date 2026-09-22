using System.Reflection;
using Hba.BuildingBlocks.Application.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace Hba.Payment.Application.Extensions;

public static class PaymentApplicationExtensions
{
    public static IServiceCollection AddPaymentApplication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddHbaApplication(Assembly.GetExecutingAssembly());

        return services;
    }
}
