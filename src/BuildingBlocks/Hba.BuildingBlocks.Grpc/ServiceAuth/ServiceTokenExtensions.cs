using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Hba.BuildingBlocks.Grpc.ServiceAuth;

public static class ServiceTokenExtensions
{
    /// <summary>
    /// Donne au service une identité propre pour ses appels d'arrière-plan.
    ///
    /// LE DEMARRAGE EST REFUSE SI LE SECRET MANQUE. Un secret vide ne se voit
    /// pas : le service démarre, tourne, et n'échoue qu'au premier événement
    /// Kafka — c'est-à-dire en production, sur une vraie course.
    /// </summary>
    public static IServiceCollection AddHbaServiceToken(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddHttpContextAccessor();

        services.AddOptions<ServiceTokenOptions>()
            .Bind(configuration.GetSection(ServiceTokenOptions.SectionName))
            .Validate(
                o => !string.IsNullOrWhiteSpace(o.ClientId),
                "ServiceToken:ClientId absent : le service ne peut pas s'authentifier aupres d'Identity.")
            .Validate(
                o => !string.IsNullOrWhiteSpace(o.ClientSecret),
                "ServiceToken:ClientSecret absent : les appels sortants d'arriere-plan seraient tous refuses.")
            .ValidateOnStart();

        services.AddSingleton<IServiceTokenProvider, IdentityServiceTokenProvider>();

        return services;
    }
}
