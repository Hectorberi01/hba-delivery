using System.Diagnostics;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Hba.BuildingBlocks.Domain;
using Microsoft.Extensions.Logging;

namespace Hba.BuildingBlocks.Grpc.Interceptors;

/// <summary>
/// Traduit les exceptions de domaine en statuts gRPC, en conservant le code
/// métier dans les trailers. Les BFF peuvent ainsi le remonter aux applications
/// sans avoir à interpréter un message en français.
///
/// IL RATTRAPE AUSSI TOUT LE RESTE, ET CE N'EST PAS DU CONFORT. Une exception
/// non métier qui s'échappe d'ici ne devient pas une erreur gRPC : Kestrel
/// renvoie un 500 HTTP nu, sans cadrage gRPC, et le client ne lit plus qu'un
/// « Bad gRPC response. HTTP status code: 500 » qui ne dit rien de la panne.
/// La trace, elle, reste dans les journaux du service appelé — introuvable
/// depuis l'application. D'où la règle : aucune exception ne sort d'ici sans
/// avoir été journalisée avec sa pile ET renvoyée avec une référence que
/// l'utilisateur peut recopier.
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
        catch (RpcException)
        {
            // Déjà cadrée — typiquement l'échec d'un appel sortant vers un autre
            // service. La réécrire effacerait son statut et son code.
            throw;
        }
        catch (Exception ex)
        {
            var reference = Reference(context);

            logger.LogError(
                ex,
                "Echec non metier sur {Method}. Reference {Reference}.",
                context.Method,
                reference);

            // LE MESSAGE NE DIT PAS CE QUI A CASSE, ET C'EST VOLONTAIRE : une
            // exception brute expose des noms de tables, des hôtes internes et
            // parfois des valeurs. La référence suffit à retrouver la pile dans
            // les journaux.
            throw new RpcException(
                new Status(StatusCode.Internal, $"Erreur interne du service. Référence : {reference}"),
                BuildTrailers("INTERNAL_ERROR"));
        }
    }

    /// <summary>
    /// La corrélation propagée par <see cref="CorrelationClientInterceptor"/> si
    /// elle existe, sinon la trace courante, sinon un identifiant neuf : il faut
    /// toujours rendre quelque chose à recopier.
    /// </summary>
    private static string Reference(ServerCallContext context)
        => context.RequestHeaders?.GetValue(GrpcMetadataKeys.CorrelationId)
           ?? Activity.Current?.RootId
           ?? Guid.NewGuid().ToString("N");

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
