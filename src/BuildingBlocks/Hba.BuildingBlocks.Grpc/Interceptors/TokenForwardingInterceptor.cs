using Grpc.Core;
using Grpc.Core.Interceptors;
using Microsoft.AspNetCore.Http;

namespace Hba.BuildingBlocks.Grpc.Interceptors;

/// <summary>
/// Reporte le JWT de l'utilisateur final sur les appels sortants. C'est ce qui
/// permet au service appelé de refaire lui-même la vérification d'autorisation,
/// au lieu de faire confiance au BFF.
/// </summary>
public sealed class TokenForwardingInterceptor(IHttpContextAccessor accessor) : Interceptor
{
    public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
        TRequest request,
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncUnaryCallContinuation<TRequest, TResponse> continuation)
    {
        ArgumentNullException.ThrowIfNull(continuation);

        var incoming = accessor.HttpContext?.Request.Headers.Authorization.ToString();

        if (!string.IsNullOrWhiteSpace(incoming))
        {
            var headers = context.Options.Headers ?? [];
            if (headers.Get(GrpcMetadataKeys.Authorization) is null)
            {
                headers.Add(GrpcMetadataKeys.Authorization, incoming);
            }

            var options = context.Options.WithHeaders(headers);
            context = new ClientInterceptorContext<TRequest, TResponse>(context.Method, context.Host, options);
        }

        return continuation(request, context);
    }
}
