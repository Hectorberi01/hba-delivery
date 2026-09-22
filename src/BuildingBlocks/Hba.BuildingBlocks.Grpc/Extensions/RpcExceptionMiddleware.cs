using Grpc.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Hba.BuildingBlocks.Grpc.Extensions;

/// <summary>
/// Traduit les erreurs gRPC remontées des services en réponses HTTP pour les
/// BFF. Le code métier stable voyage dans les trailers : c'est lui qui est
/// renvoyé aux applications, pas le message en français.
/// </summary>
public static class RpcExceptionMiddleware
{
    public static IApplicationBuilder UseRpcExceptionTranslation(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        return app.Use(async (context, next) =>
        {
            try
            {
                await next(context).ConfigureAwait(false);
            }
            catch (RpcException ex)
            {
                var logger = context.RequestServices
                    .GetService(typeof(ILoggerFactory)) as ILoggerFactory;

                logger?.CreateLogger("Bff").LogInformation(
                    "Appel interne refusé : {StatusCode} — {Detail}",
                    ex.StatusCode,
                    ex.Status.Detail);

                context.Response.StatusCode = MapHttpStatus(ex.StatusCode);
                context.Response.ContentType = "application/problem+json";

                await context.Response.WriteAsJsonAsync(new
                {
                    code = ex.Trailers.GetValue(GrpcMetadataKeys.ErrorCode) ?? ex.StatusCode.ToString(),
                    message = ex.Status.Detail,
                }).ConfigureAwait(false);
            }
        });
    }

    private static int MapHttpStatus(StatusCode code) => code switch
    {
        StatusCode.NotFound => StatusCodes.Status404NotFound,
        StatusCode.PermissionDenied => StatusCodes.Status403Forbidden,
        StatusCode.Unauthenticated => StatusCodes.Status401Unauthorized,
        StatusCode.InvalidArgument => StatusCodes.Status400BadRequest,
        StatusCode.FailedPrecondition => StatusCodes.Status409Conflict,
        StatusCode.AlreadyExists => StatusCodes.Status409Conflict,
        StatusCode.DeadlineExceeded => StatusCodes.Status504GatewayTimeout,
        StatusCode.Unavailable => StatusCodes.Status503ServiceUnavailable,
        StatusCode.ResourceExhausted => StatusCodes.Status429TooManyRequests,
        _ => StatusCodes.Status500InternalServerError,
    };
}
