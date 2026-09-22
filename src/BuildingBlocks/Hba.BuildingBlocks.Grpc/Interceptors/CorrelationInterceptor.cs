using System.Diagnostics;
using Grpc.Core;
using Grpc.Core.Interceptors;

namespace Hba.BuildingBlocks.Grpc.Interceptors;

/// <summary>
/// Propage l'identifiant de corrélation d'appel en appel. Le TraceId vient
/// d'OpenTelemetry ; la corrélation, elle, suit le parcours métier, y compris
/// à travers Kafka.
/// </summary>
public sealed class CorrelationClientInterceptor : Interceptor
{
    public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
        TRequest request,
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncUnaryCallContinuation<TRequest, TResponse> continuation)
    {
        ArgumentNullException.ThrowIfNull(continuation);

        var headers = context.Options.Headers ?? [];

        if (headers.Get(GrpcMetadataKeys.CorrelationId) is null)
        {
            var correlationId = Activity.Current?.RootId ?? Guid.NewGuid().ToString("N");
            headers.Add(GrpcMetadataKeys.CorrelationId, correlationId);
        }

        var options = context.Options.WithHeaders(headers);
        var newContext = new ClientInterceptorContext<TRequest, TResponse>(context.Method, context.Host, options);

        return continuation(request, newContext);
    }
}
