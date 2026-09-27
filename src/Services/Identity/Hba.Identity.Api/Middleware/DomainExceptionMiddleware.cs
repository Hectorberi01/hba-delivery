using System.Diagnostics;
using Hba.BuildingBlocks.Domain;

namespace Hba.Identity.Api.Middleware;

/// <summary>
/// Equivalent HTTP de l'ExceptionInterceptor gRPC.
///
/// IL N'EST PAS FACULTATIF. Les routes REST d'Identity passent par les mêmes
/// handlers que le gRPC, donc elles lèvent les mêmes DomainException. Sans ce
/// middleware, une règle métier refusée — « code incorrect », « compte
/// suspendu » — sortirait en 500 nu, alors que par gRPC elle sort en refus
/// cadré. Deux transports qui répondent différemment à la même règle, c'est la
/// dérive que l'on cherche justement à éviter en ouvrant cette seconde surface.
///
/// Le corps est celui des passerelles : { code, message }. Le code est stable
/// et fait contrat ; le message est en français et peut changer.
/// </summary>
public static class DomainExceptionMiddleware
{
    public static IApplicationBuilder UseHbaDomainExceptions(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        return app.Use(async (context, next) =>
        {
            try
            {
                await next(context).ConfigureAwait(false);
            }
            catch (DomainException ex)
            {
                var logger = context.RequestServices.GetRequiredService<ILoggerFactory>()
                    .CreateLogger("Identity.Http");

                logger.LogInformation(
                    "Règle métier refusée sur {Path} : {Code} — {Message}",
                    context.Request.Path,
                    ex.Code,
                    ex.Message);

                await WriteAsync(context, MapStatus(ex), ex.Code, ex.Message).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                var reference = Activity.Current?.RootId ?? context.TraceIdentifier;

                context.RequestServices.GetRequiredService<ILoggerFactory>()
                    .CreateLogger("Identity.Http")
                    .LogError(ex, "Echec non metier sur {Path}. Reference {Reference}.", context.Request.Path, reference);

                // Le message ne dit pas ce qui a cassé : une exception brute
                // expose des noms de tables et des hôtes internes.
                await WriteAsync(
                    context,
                    StatusCodes.Status500InternalServerError,
                    "INTERNAL_ERROR",
                    $"Erreur interne du service. Référence : {reference}").ConfigureAwait(false);
            }
        });
    }

    private static async Task WriteAsync(HttpContext context, int status, string code, string message)
    {
        // Si la réponse a déjà commencé, on ne peut plus rien réécrire : la
        // seule chose honnête est de laisser l'exception couper la connexion
        // plutôt que d'écrire un corps par-dessus un autre.
        if (context.Response.HasStarted)
        {
            return;
        }

        context.Response.StatusCode = status;
        context.Response.ContentType = "application/problem+json";

        await context.Response.WriteAsJsonAsync(new { code, message }).ConfigureAwait(false);
    }

    /// <summary>
    /// MEME TABLE QUE LE GRPC, VOLONTAIREMENT. ExceptionInterceptor traduit en
    /// statuts gRPC, que RpcExceptionMiddleware retraduit ensuite en HTTP dans
    /// les passerelles. Ici on compose les deux d'un coup, et le résultat doit
    /// être identique : un même refus ne peut pas valoir 409 par une porte et
    /// 400 par l'autre.
    /// </summary>
    private static int MapStatus(DomainException exception) => exception switch
    {
        NotFoundException => StatusCodes.Status404NotFound,
        ForbiddenException => StatusCodes.Status403Forbidden,
        InvalidStateTransitionException => StatusCodes.Status409Conflict,
        _ when exception.Code == "VALIDATION_FAILED" => StatusCodes.Status400BadRequest,
        _ => StatusCodes.Status409Conflict,
    };
}
