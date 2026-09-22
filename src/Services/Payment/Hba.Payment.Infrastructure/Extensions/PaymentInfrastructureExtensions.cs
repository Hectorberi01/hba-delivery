using Hba.BuildingBlocks.Messaging.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Hba.Payment.Infrastructure.Extensions;

public static class PaymentInfrastructureExtensions
{
    /// <summary>
    /// Intentions de paiement et remboursements. Contient l'adaptateur FedaPay.
    /// La persistance n'est pas encore posee : seule la messagerie commune l'est,
    /// pour que le service demarre et publie son Outbox des qu'il aura un modele.
    /// </summary>
    public static IServiceCollection AddPaymentInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddHbaMessaging(configuration, producerName: "payment", consumerGroupId: "payment");

        return services;
    }
}
