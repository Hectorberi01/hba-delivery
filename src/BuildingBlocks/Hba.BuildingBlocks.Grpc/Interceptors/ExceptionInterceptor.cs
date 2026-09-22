using Grpc.Core;
using Grpc.Core.Interceptors;
using Hba.BuildingBlocks.Domain;
using Microsoft.Extensions.Logging;

namespace Hba.BuildingBlocks.Grpc.Interceptors;

/// <summary>
/// Traduit les exceptions de domaine en statuts gRPC, en conservant le code
/// métier dans les trailers. Les BFF peuvent ainsi le remonter aux applications
/// sans avoir à interpréter un message en français.
/// </summary>
public sealed class ExceptionInterceptor(ILogger<ExceptionInterceptor> logger) : Interceptor
{
    public override async Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
        TRequest request,
        ServerCallContext context,
        UnaryServerMethod<TRequest, TResponse> continuation)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(continuation);

        try
        {
            return await continuation(request, context).ConfigureAwait(false);
        }
        catch (DomainException ex)
        {
            logger.LogInformation(
                "Règle métier refusée sur {Method} : {Code} — {Message}",
                context.Method,
                ex.Code,
                ex.Message);

            throw new RpcException(new Status(MapStatusCode(ex), ex.Message), BuildTrailers(ex.Code));
        }
        catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
        {
            throw new RpcException(new Status(StatusCode.Cancelled, "Appel annulé."));
        }
    }

    private static StatusCode MapStatusCode(DomainException exception) => exception switch
    {
        NotFoundException => StatusCode.NotFound,
        ForbiddenException => StatusCode.PermissionDenied,
        InvalidStateTransitionException => StatusCode.FailedPrecondition,
        _ when exception.Code == "VALIDATION_FAILED" => StatusCode.InvalidArgument,
        _ => StatusCode.FailedPrecondition,
    };

    private static Metadata BuildTrailers(string code) => new() { { GrpcMetadataKeys.ErrorCode, code } };
}
