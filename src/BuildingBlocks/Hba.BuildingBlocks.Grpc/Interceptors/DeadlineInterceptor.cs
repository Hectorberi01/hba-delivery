using Grpc.Core;
using Grpc.Core.Interceptors;

namespace Hba.BuildingBlocks.Grpc.Interceptors;

/// <summary>
/// Impose une échéance à tout appel sortant qui n'en a pas. Sans cela, un
/// service lent bloque ses appelants jusqu'à saturation du pool de connexions.
/// </summary>
public sealed class DeadlineClientInterceptor(TimeSpan defaultDeadline) : Interceptor
{
    public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
        TRequest request,
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncUnaryCallContinuation<TRequest, TResponse> continuation)
    {
        ArgumentNullException.ThrowIfNull(continuation);

        if (context.Options.Deadline is not null)
        {
            return continuation(request, context);
        }

        var options = context.Options.WithDeadline(DateTime.UtcNow.Add(defaultDeadline));
        var newContext = new ClientInterceptorContext<TRequest, TResponse>(context.Method, context.Host, options);

        return continuation(request, newContext);
    }
}
