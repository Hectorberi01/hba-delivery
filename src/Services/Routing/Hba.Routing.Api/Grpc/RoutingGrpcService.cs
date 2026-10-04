using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Hba.BuildingBlocks.Application.Messaging;
using Hba.BuildingBlocks.Domain;
using Hba.Contracts.Routing.V1;
using Hba.Routing.Application.Features.Routes;
using Microsoft.AspNetCore.Authorization;
using DomainProfile = Hba.Routing.Domain.Routes.RouteProfile;
using DomainRoute = Hba.Routing.Domain.Routes.Route;
using ProtoProfile = Hba.Contracts.Routing.V1.RouteProfile;
using ProtoRoute = Hba.Contracts.Routing.V1.Route;

namespace Hba.Routing.Api.Grpc;

/// <summary>
/// Entrée synchrone du service Routing.
/// </summary>
///
/// <remarks>
/// AUCUN APPELANT PUBLIC : ce service n'est joignable que par les autres
/// services, à travers la passerelle qui ne l'expose pas. Un itinéraire coûte
/// un calcul, et l'ouvrir au dehors reviendrait à offrir un moteur de routage
/// gratuit à qui possède un jeton.
/// </remarks>
[Authorize]
public sealed class RoutingGrpcService(IDispatcher dispatcher) : RoutingService.RoutingServiceBase
{
    public override async Task<ProtoRoute> CalculateRoute(
        CalculateRouteRequest request,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        if (request.Origin is null || request.Destination is null)
        {
            throw new DomainException(
                "VALIDATION_FAILED",
                "Un itineraire demande une origine et une destination.");
        }

        var trajet = await dispatcher.QueryAsync(
            new CalculateRouteQuery(
                request.Origin.Latitude,
                request.Origin.Longitude,
                request.Destination.Latitude,
                request.Destination.Longitude,
                ToDomain(request.Profile),
                request.IncludePolyline),
            context.CancellationToken).ConfigureAwait(false);

        return ToProto(trajet);
    }

    public override async Task<Hba.Contracts.Routing.V1.RouteMatrix> CalculateMatrix(
        CalculateMatrixRequest request,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        var matrice = await dispatcher.QueryAsync(
            new CalculateMatrixQuery(
                [.. request.Origins.Select(p => (p.Latitude, p.Longitude))],
                [.. request.Destinations.Select(p => (p.Latitude, p.Longitude))],
                ToDomain(request.Profile)),
            context.CancellationToken).ConfigureAwait(false);

        var reponse = new Hba.Contracts.Routing.V1.RouteMatrix
        {
            Provider = matrice.Provider,
            CalculatedAt = Timestamp.FromDateTimeOffset(matrice.CalculatedAt),
            FromFallback = matrice.FromFallback,
        };

        foreach (var ligne in matrice.Rows)
        {
            var row = new MatrixRow();

            row.Cells.AddRange(ligne.Select(c => new MatrixCell
            {
                DistanceMeters = c.DistanceMeters,
                DurationSeconds = c.DurationSeconds,
                Reachable = c.Reachable,
            }));

            reponse.Rows.Add(row);
        }

        return reponse;
    }

    /// <summary>
    /// Le profil non precise devient la voiture.
    ///
    /// CE REPLI EST LE MOINS FAUX DES TROIS. Une voiture emprunte le reseau le
    /// plus contraint : son itineraire est le plus long et sa duree la plus
    /// pessimiste. Un appelant qui oublie de preciser obtient donc une mesure
    /// prudente, jamais une sous-estimation.
    /// </summary>
    private static DomainProfile ToDomain(ProtoProfile profile) => profile switch
    {
        ProtoProfile.Motorcycle => DomainProfile.Motorcycle,
        ProtoProfile.Bicycle => DomainProfile.Bicycle,
        _ => DomainProfile.Car,
    };

    private static ProtoRoute ToProto(DomainRoute route) => new()
    {
        DistanceMeters = route.DistanceMeters,
        DurationSeconds = route.DurationSeconds,
        EncodedPolyline = route.EncodedPolyline,
        Provider = route.Provider,
        CalculatedAt = Timestamp.FromDateTimeOffset(route.CalculatedAt),
        FromFallback = route.FromFallback,
    };
}
