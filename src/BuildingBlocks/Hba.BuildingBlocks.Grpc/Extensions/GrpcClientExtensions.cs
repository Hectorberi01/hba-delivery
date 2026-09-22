using Grpc.Net.ClientFactory;
using Hba.BuildingBlocks.Grpc.Interceptors;
using Microsoft.Extensions.DependencyInjection;

namespace Hba.BuildingBlocks.Grpc.Extensions;

public static class GrpcClientExtensions
{
    /// <summary>
    /// Client gRPC interne : échéance par défaut, corrélation, report du JWT.
    /// </summary>
    public static IHttpClientBuilder AddHbaGrpcClient<TClient>(
        this IServiceCollection services,
        Uri address,
        TimeSpan? deadline = null)
        where TClient : class
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddHttpContextAccessor();
        services.AddSingleton<CorrelationClientInterceptor>();
        services.AddSingleton<TokenForwardingInterceptor>();
        services.AddSingleton(new DeadlineClientInterceptor(deadline ?? TimeSpan.FromSeconds(5)));

        return services
            .AddGrpcClient<TClient>(o => o.Address = address)
            .AddInterceptor<CorrelationClientInterceptor>(InterceptorScope.Client)
            .AddInterceptor<TokenForwardingInterceptor>(InterceptorScope.Client)
            .AddInterceptor<DeadlineClientInterceptor>(InterceptorScope.Client);
    }
}
