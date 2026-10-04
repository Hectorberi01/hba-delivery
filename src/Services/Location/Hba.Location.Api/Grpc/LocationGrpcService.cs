using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Hba.BuildingBlocks.Application.Messaging;
using Hba.BuildingBlocks.Domain;
using Hba.Contracts.Location.V1;
using Hba.Location.Application.Common;
using Hba.Location.Application.Features.Positions;
using Hba.Location.Domain.Exceptions;
using Microsoft.AspNetCore.Authorization;
using ProtoGeoPoint = Hba.Contracts.Common.V1.GeoPoint;
using ProtoPosition = Hba.Contracts.Location.V1.DriverPosition;

namespace Hba.Location.Api.Grpc;

/// <summary>
/// Entree synchrone du service Location.
/// </summary>
///
/// <remarks>
/// CE SERVICE NE SAIT PAS QUI EST LE CHAUFFEUR, et aucune methode de ce fichier ne
/// peut le lui apprendre : il n'a ni base, ni client vers Driver. Il tient des points
/// et leur fraicheur. La question « qui peut prendre cette course » se pose a Driver,
/// par celui qui a interroge les deux.
///
/// L'AUTORISATION N'EST PAS ICI, ET CE N'EST PAS UN OUBLI. Le [Authorize] de la classe
/// etablit qu'il y a un appelant ; QUI a le droit de demander ou se tient quelqu'un est
/// decide dans LocationAccess, appele par chaque handler. L'autorisation metier vit
/// dans la couche Application, ou elle est testable sans serveur gRPC (ADR 0007).
/// </remarks>
[Authorize]
public sealed class LocationGrpcService(IDispatcher dispatcher) : LocationService.LocationServiceBase
{
    public override async Task<UpdatePositionResponse> UpdatePosition(
        UpdatePositionRequest request,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        if (request.Point is null)
        {
            throw new DomainException(
                "MISSING_POSITION",
                "Une mise a jour de position transporte une position.");
        }

        var ok = await dispatcher.SendAsync(
            new UpdatePositionCommand(
                ParseId(request.DriverId),
                request.Point.Latitude,
                request.Point.Longitude,
                request.CapturedAt?.ToDateTimeOffset(),

                // LES TROIS CHAMPS SONT « optional » AU CONTRAT, ET C'EST CE QUI PERMET
                // DE LES PASSER TELS QUELS. Sans la presence explicite, un cap absent
                // et un cap de zero degre arriveraient tous deux a zero : toutes les
                // motos immobiles pointeraient vers le nord sur la carte.
                request.HasHeadingDegrees ? request.HeadingDegrees : null,
                request.HasSpeedMps ? request.SpeedMps : null,
                request.HasAccuracyMeters ? request.AccuracyMeters : null),
            context.CancellationToken).ConfigureAwait(false);

        return new UpdatePositionResponse { Ok = ok };
    }

    public override async Task<ForgetPositionResponse> ForgetPosition(
        ForgetPositionRequest request,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        var ok = await dispatcher.SendAsync(
            new ForgetPositionCommand(ParseId(request.DriverId)),
            context.CancellationToken).ConfigureAwait(false);

        return new ForgetPositionResponse { Ok = ok };
    }

    public override async Task<PositionLookup> GetPosition(
        GetPositionRequest request,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        var vue = await dispatcher.QueryAsync(
            new GetPositionQuery(
                ParseId(request.DriverId),
                request.Reference?.Latitude,
                request.Reference?.Longitude),
            context.CancellationToken).ConfigureAwait(false);

        var reponse = new PositionLookup
        {
            Found = vue.Found,
            FreshnessSeconds = vue.FreshnessSeconds,
            DistanceMeters = vue.DistanceMeters ?? 0,
        };

        if (vue.Position is not null)
        {
            reponse.Position = ToProto(vue.Position);
        }

        return reponse;
    }

    public override async Task<FindNearbyDriversResponse> FindNearbyDrivers(
        FindNearbyDriversRequest request,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        if (request.Center is null)
        {
            throw new DomainException(
                "MISSING_POSITION",
                "Une recherche de proximite part d'un point.");
        }

        var vue = await dispatcher.QueryAsync(
            new FindNearbyDriversQuery(
                request.Center.Latitude,
                request.Center.Longitude,
                request.RadiusMeters,
                request.Limit,

                // UN IDENTIFIANT D'EXCLUSION MAL FORME EST IGNORE, PAS REFUSE, et c'est
                // la seule tolerance de ce fichier. Faire echouer une vague entiere
                // parce qu'un identifiant de tour precedent est illisible la bloquerait
                // definitivement : le meme appel reviendrait avec la meme liste. Ignorer
                // le nom illisible, au pire, repropose la course a quelqu'un qui vient
                // de la refuser.
                [.. request.ExcludeDriverIds
                    .Select(id => Guid.TryParse(id, out var exclu) ? exclu : Guid.Empty)
                    .Where(id => id != Guid.Empty)]),
            context.CancellationToken).ConfigureAwait(false);

        var reponse = new FindNearbyDriversResponse { FreshnessSeconds = vue.FreshnessSeconds };

        reponse.Drivers.AddRange(vue.Drivers.Select(d => new NearbyDriver
        {
            DriverId = d.Position.DriverId.ToString(),
            DistanceMeters = d.DistanceMeters,
            Position = ToProto(d.Position),
        }));

        return reponse;
    }

    public override async Task<ListFreshPositionsResponse> ListFreshPositions(
        ListFreshPositionsRequest request,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        var vue = await dispatcher.QueryAsync(
            new ListFreshPositionsQuery(request.Limit),
            context.CancellationToken).ConfigureAwait(false);

        var reponse = new ListFreshPositionsResponse { FreshnessSeconds = vue.FreshnessSeconds };

        reponse.Positions.AddRange(vue.Positions.Select(ToProto));

        return reponse;
    }

    /// <summary>
    /// Un identifiant mal forme est une erreur d'appelant, pas une panne : il devient
    /// INVALID_ARGUMENT et non une exception de parsing remontee en INTERNAL avec une
    /// reference de support.
    /// </summary>
    private static Guid ParseId(string value)
        => Guid.TryParse(value, out var id) && id != Guid.Empty
            ? id
            : throw new DomainException(
                LocationErrorCodes.InvalidId,
                "Le champ driver_id n'est pas un identifiant valide.");

    private static ProtoPosition ToProto(PositionView vue)
    {
        var position = new ProtoPosition
        {
            DriverId = vue.DriverId.ToString(),
            Point = new ProtoGeoPoint { Latitude = vue.Latitude, Longitude = vue.Longitude },
            CapturedAt = Timestamp.FromDateTimeOffset(vue.CapturedAt),
            SeenAt = Timestamp.FromDateTimeOffset(vue.SeenAt),
        };

        // LES TROIS CHAMPS NE SONT POSES QUE S'ILS EXISTENT. Les affecter a zero quand
        // ils sont absents les rendrait PRESENTS au contrat — c'est tout l'interet de
        // « optional » — et le lecteur verrait un cap plein nord et une vitesse nulle
        // la ou le telephone n'a simplement rien dit.
        if (vue.HeadingDegrees is { } cap)
        {
            position.HeadingDegrees = cap;
        }

        if (vue.SpeedMps is { } vitesse)
        {
            position.SpeedMps = vitesse;
        }

        if (vue.AccuracyMeters is { } precision)
        {
            position.AccuracyMeters = precision;
        }

        return position;
    }
}
