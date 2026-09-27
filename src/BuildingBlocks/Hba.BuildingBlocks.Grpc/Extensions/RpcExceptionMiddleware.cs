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

                var bffLogger = logger?.CreateLogger("Bff");
                var fault = IsFault(ex.StatusCode);

                if (fault)
                {
                    // UNE PANNE N'EST PAS UN REFUS. La journaliser en Information
                    // la noie dans le trafic normal et fait croire que le BFF a
                    // fonctionné.
                    bffLogger?.LogError(
                        ex,
                        "Appel interne en échec : {StatusCode} — {Detail}",
                        ex.StatusCode,
                        ex.Status.Detail);
                }
                else
                {
                    bffLogger?.LogInformation(
                        "Appel interne refusé : {StatusCode} — {Detail}",
                        ex.StatusCode,
                        ex.Status.Detail);
                }

                context.Response.StatusCode = MapHttpStatus(ex.StatusCode);
                context.Response.ContentType = "application/problem+json";

                await context.Response.WriteAsJsonAsync(new
                {
                    code = ex.Trailers.GetValue(GrpcMetadataKeys.ErrorCode) ?? ex.StatusCode.ToString(),
                    message = PublicMessage(ex),
                }).ConfigureAwait(false);
            }
        });
    }

    /// <summary>
    /// Vrai quand le statut décrit une panne, pas une règle métier.
    /// </summary>
    private static bool IsFault(StatusCode code) => code
        is StatusCode.Internal
        or StatusCode.Unknown
        or StatusCode.Unavailable
        or StatusCode.DataLoss
        or StatusCode.Unimplemented
        or StatusCode.DeadlineExceeded;

    /// <summary>
    /// Ce que l'application a le droit de lire.
    ///
    /// UNKNOWN EST UN CAS A PART : ce statut n'est jamais émis par un service,
    /// il est fabriqué par le client gRPC quand la réponse n'est pas du gRPC du
    /// tout. Son détail est alors du bruit de transport — « Bad gRPC response.
    /// HTTP status code: 500 » — qui décrit le tuyau, pas la panne, et n'a rien
    /// à faire sur un écran de téléphone.
    /// </summary>
    private static string PublicMessage(RpcException exception) => exception.StatusCode switch
    {
        StatusCode.Unknown => "Service momentanément indisponible. Réessayez dans un instant.",
        StatusCode.Unavailable => "Service momentanément indisponible. Réessayez dans un instant.",
        _ => exception.Status.Detail,
    };

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
