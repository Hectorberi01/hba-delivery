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
        // L'ECHEANCE EST PROPRE A CHAQUE CLIENT, ET ELLE NE L'ETAIT PAS.
        //
        // La version precedente faisait « AddSingleton(new
        // DeadlineClientInterceptor(...)) » puis « AddInterceptor<T> », qui
        // resout PAR TYPE : le dernier enregistrement l'emportait pour TOUS
        // les clients. Dans la passerelle, Delivery declarait trente secondes
        // en premier et Payment cinq en dernier — donc tout le monde avait
        // cinq secondes, y compris Delivery, dont le commentaire explique
        // justement pourquoi trente sont necessaires.
        //
        // PERSONNE NE POUVAIT LE VOIR : le service repondait, simplement plus
        // tot qu'il n'aurait du, et l'appelant lisait « DeadlineExceeded » sur
        // une operation qui allait aboutir. On passe donc l'instance a
        // l'enregistrement du client, ou elle ne concerne que lui.
        var echeance = new DeadlineClientInterceptor(deadline ?? TimeSpan.FromSeconds(5));

        return services
            .AddGrpcClient<TClient>(o => o.Address = address)
            .AddInterceptor<CorrelationClientInterceptor>(InterceptorScope.Client)
            .AddInterceptor<TokenForwardingInterceptor>(InterceptorScope.Client)
            .AddInterceptor(InterceptorScope.Client, _ => echeance);
    }
}
