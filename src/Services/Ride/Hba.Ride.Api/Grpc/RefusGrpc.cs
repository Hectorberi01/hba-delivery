using Grpc.Core;
using Hba.BuildingBlocks.Grpc;
using Hba.Ride.Application.Authorization;
using Hba.Ride.Domain.Exceptions;
using Hba.Ride.Domain.Results;

namespace Hba.Ride.Api.Grpc;

/// <summary>
/// Traduit un <c>Result</c> refusé en <c>RpcException</c>.
/// </summary>
///
/// <remarks>
/// C'EST ICI, ET NULLE PART AILLEURS, QUE LA CONVENTION DE RIDE DEVIENT INVISIBLE
/// DU DEHORS. Le point 35 autorise <c>Result</c> dans ce service À UNE CONDITION
/// STRICTE : un refus de Ride doit produire exactement le même statut gRPC qu'un
/// refus de Delivery. Les règles sont donc recopiées de
/// <c>ExceptionInterceptor</c>, y compris le trailer qui porte le code métier —
/// sans lui, la passerelle ne pourrait plus distinguer deux refus partageant le
/// même statut.
///
/// SI CES DEUX LISTES DIVERGENT, LA COHABITATION TOMBE. Un code ajouté à
/// <see cref="RideErrorCodes"/> sans décision de statut atterrit en
/// <c>FAILED_PRECONDITION</c>, comme le fait la branche par défaut de
/// l'intercepteur : c'est le même comportement, pas un oubli.
/// </remarks>
internal static class RefusGrpc
{
    /// <summary>La valeur, ou une exception gRPC si c'est un refus.</summary>
    internal static TValue Exiger<TValue>(this Result<TValue> result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return result.Succeeded ? result.Value : throw Lever(result.Error);
    }

    /// <summary>Lève si c'est un refus, et ne rend rien sinon.</summary>
    internal static void Exiger(this Result result)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (result.Failed)
        {
            throw Lever(result.Error);
        }
    }

    private static RpcException Lever(Error error)
        => new(
            new Status(Statut(error.Code), error.Message),
            new Metadata { { GrpcMetadataKeys.ErrorCode, error.Code } });

    private static StatusCode Statut(string code) => code switch
    {
        RideAccess.Forbidden => StatusCode.PermissionDenied,
        RideErrorCodes.ValidationFailed => StatusCode.InvalidArgument,
        _ => StatusCode.FailedPrecondition,
    };
}
