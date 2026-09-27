using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Hba.BuildingBlocks.Application.Messaging;
using Hba.BuildingBlocks.Application.Time;
using Hba.BuildingBlocks.Domain;
using Hba.BuildingBlocks.Grpc.Time;
using Hba.Contracts.Driver.V1;
using Hba.Driver.Application.Common.Views;
using Hba.Driver.Application.Features.Drivers.Commands;
using Hba.Driver.Application.Features.Drivers.Queries;
using Microsoft.AspNetCore.Authorization;
using CommonVehicleType = Hba.Contracts.Common.V1.VehicleType;
using DomainDocumentType = Hba.Driver.Domain.Drivers.DocumentType;
using DomainOperational = Hba.Driver.Domain.Drivers.OperationalStatus;
using DomainVehicleType = Hba.Driver.Domain.Drivers.VehicleType;
using DomainVerification = Hba.Driver.Domain.Drivers.VerificationStatus;
using ProtoDriver = Hba.Contracts.Driver.V1.Driver;
using ProtoDriverPosition = Hba.Contracts.Driver.V1.DriverPosition;

namespace Hba.Driver.Api.Grpc;

/// <summary>
/// Entree synchrone du service Driver.
///
/// L'AUTORISATION FINE EST DANS LES HANDLERS, pas ici : l'ADR 0007 exige
/// qu'elle soit verifiee cote service, et un attribut par methode ne saurait
/// pas dire « ce livreur-ci, et pas un autre ».
/// </summary>
[Authorize]
public sealed class DriverGrpcService(IDispatcher dispatcher, ITimeCalendar calendrier)
    : DriverService.DriverServiceBase
{
    public override async Task<ProtoDriver> GetDriver(GetDriverRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        var view = await dispatcher.QueryAsync(
            new GetDriverQuery(ParseId(request.DriverId)),
            context.CancellationToken).ConfigureAwait(false);

        return ToProto(view);
    }

    public override async Task<DriverPublicProfile> GetDriverPublicProfile(
        GetDriverRequest request,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        var view = await dispatcher.QueryAsync(
            new GetDriverPublicProfileQuery(ParseId(request.DriverId)),
            context.CancellationToken).ConfigureAwait(false);

        return new DriverPublicProfile
        {
            DriverId = view.DriverId.ToString(),
            DisplayName = view.DisplayName,
            Phone = view.Phone,
            VehicleType = ToProto(view.VehicleType),
            VehiclePlate = view.VehiclePlate,
        };
    }

    public override async Task<ProtoDriver> GoOnline(GoOnlineRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        if (request.Position is null)
        {
            throw new DomainException(
                "MISSING_POSITION",
                "Passer en ligne demande une position : sans elle aucune course ne peut etre proposee.");
        }

        var view = await dispatcher.SendAsync(
            new GoOnlineCommand(ParseId(request.DriverId), request.Position.Latitude, request.Position.Longitude),
            context.CancellationToken).ConfigureAwait(false);

        return ToProto(view);
    }

    public override async Task<ProtoDriver> GoOffline(GoOfflineRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        var view = await dispatcher.SendAsync(
            new GoOfflineCommand(ParseId(request.DriverId)),
            context.CancellationToken).ConfigureAwait(false);

        return ToProto(view);
    }

    public override async Task<UpdateLocationResponse> UpdateLocation(
        UpdateLocationRequest request,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        if (request.Position is null)
        {
            throw new DomainException("MISSING_POSITION", "Une mise a jour de position transporte une position.");
        }

        var ok = await dispatcher.SendAsync(
            new UpdateDriverLocationCommand(
                ParseId(request.DriverId),
                request.Position.Latitude,
                request.Position.Longitude,
                request.CapturedAt?.ToDateTimeOffset()),
            context.CancellationToken).ConfigureAwait(false);

        return new UpdateLocationResponse { Ok = ok };
    }

    public override async Task<DriverAvailability> CheckDriverAvailability(
        CheckDriverAvailabilityRequest request,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        if (request.Reference is null)
        {
            throw new DomainException(
                "MISSING_REFERENCE",
                "Une distance se mesure depuis un point : la reference est obligatoire.");
        }

        var vue = await dispatcher.QueryAsync(
            new CheckDriverAvailabilityQuery(
                ParseId(request.DriverId),
                request.Reference.Latitude,
                request.Reference.Longitude),
            context.CancellationToken).ConfigureAwait(false);

        var reponse = new DriverAvailability
        {
            DriverId = vue.DriverId.ToString(),
            DisplayName = vue.DisplayName,
            Offerable = vue.Offerable,
            Reason = vue.Reason ?? string.Empty,
            VerificationStatus = ToProto(vue.VerificationStatus),
            OperationalStatus = ToProto(vue.OperationalStatus),
        };

        // LES TROIS CHAMPS DE POSITION RESTENT VIDES QUAND IL N'Y A PAS DE
        // POSITION RECENTE, plutot que de prendre leur valeur par defaut.
        // Une distance de zero et une date a l'epoque Unix se lisent comme
        // des mesures ; l'absence, elle, se lit comme une absence.
        if (vue.Position is { } position)
        {
            reponse.DistanceMeters = position.DistanceMeters;
            reponse.Position = new Hba.Contracts.Common.V1.GeoPoint
            {
                Latitude = position.Latitude,
                Longitude = position.Longitude,
            };
            reponse.SeenAt = Timestamp.FromDateTimeOffset(position.SeenAt);
        }

        return reponse;
    }

    public override async Task<FindAvailableNearbyResponse> FindAvailableNearby(
        FindAvailableNearbyRequest request,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        if (request.Center is null)
        {
            throw new DomainException("MISSING_CENTER", "Une recherche de proximite part d'un point.");
        }

        var exclus = request.ExcludeDriverIds
            .Select(id => Guid.TryParse(id, out var parsed) ? parsed : Guid.Empty)
            .Where(id => id != Guid.Empty)
            .ToHashSet();

        var vues = await dispatcher.QueryAsync(
            new FindAvailableNearbyQuery(
                request.Center.Latitude,
                request.Center.Longitude,
                request.RadiusMeters,
                ToDomain(request.VehicleType),
                request.Limit,
                exclus),
            context.CancellationToken).ConfigureAwait(false);

        var response = new FindAvailableNearbyResponse();

        response.Drivers.AddRange(vues.Select(v => new NearbyDriver
        {
            DriverId = v.DriverId.ToString(),
            DistanceMeters = v.DistanceMeters,
            Position = new Hba.Contracts.Common.V1.GeoPoint { Latitude = v.Latitude, Longitude = v.Longitude },
        }));

        return response;
    }

    // ------------------------------- Constitution du dossier (ADR 0021) --
    //
    // AUCUNE DE CES METHODES NE RECOIT D'OCTETS. La passerelle a deja ecrit
    // dans le stockage objet ; elle n'envoie ici que la cle. Un champ bytes
    // ferait passer une piece d'identite par les intercepteurs de trace.

    public override async Task<ProtoDriver> DeclareVehicle(
        DeclareVehicleRequest request,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        // UN TYPE DE VEHICULE INCONNU EST REFUSE ICI, et non traduit en moto
        // par defaut. Vehicle.Unknown est une moto parce qu'un profil doit
        // naitre utilisable ; une DECLARATION, elle, doit dire la verite.
        var type = ToDomain(request.Type)
            ?? throw new RpcException(new Status(
                StatusCode.InvalidArgument,
                "Le type de vehicule doit etre precise."));

        var view = await dispatcher.SendAsync(
            new DeclareVehicleCommand(
                ParseId(request.DriverId),
                type,
                request.Plate,
                request.CapacityGrams),
            context.CancellationToken).ConfigureAwait(false);

        return ToProto(view);
    }

    public override async Task<ProtoDriver> SubmitApplication(
        SubmitApplicationRequest request,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        var view = await dispatcher.SendAsync(
            new SubmitApplicationCommand(ParseId(request.DriverId)),
            context.CancellationToken).ConfigureAwait(false);

        return ToProto(view);
    }

    public override async Task<DriverApplication> GetApplication(
        GetDriverRequest request,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        var view = await dispatcher.QueryAsync(
            new GetDriverApplicationQuery(ParseId(request.DriverId)),
            context.CancellationToken).ConfigureAwait(false);

        var reponse = new DriverApplication
        {
            DriverId = view.DriverId.ToString(),
            VerificationStatus = ToProto(view.VerificationStatus),
            StatusReason = view.StatusReason,
            SubmittedAt = view.SubmittedAt is null
                ? null
                : Timestamp.FromDateTimeOffset(view.SubmittedAt.Value),
            Vehicle = new Vehicle
            {
                Type = ToProto(view.VehicleType),
                Plate = view.VehiclePlate,
                CapacityGrams = view.VehicleCapacityGrams,
            },
            ProfilePhotoUrl = view.ProfilePhotoUrl?.ToString() ?? string.Empty,
            CanSubmit = view.CanSubmit,
        };

        reponse.Documents.AddRange(view.Documents.Select(d => new DocumentSummary
        {
            Type = ToProto(d.Type),
            UploadedAt = Timestamp.FromDateTimeOffset(d.UploadedAt),
            SizeBytes = d.SizeBytes,
            ContentType = d.ContentType,
            ReadUrl = d.ReadUrl.ToString(),
            ReadUrlExpiresAt = Timestamp.FromDateTimeOffset(d.ReadUrlExpiresAt),
        }));

        reponse.MissingDocuments.AddRange(view.MissingDocuments.Select(ToProto));

        return reponse;
    }

    public override async Task<ProtoDriver> ReviewKyc(ReviewKycRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        var view = await dispatcher.SendAsync(
            new ReviewDriverKycCommand(ParseId(request.DriverId), request.Approved, request.Reason),
            context.CancellationToken).ConfigureAwait(false);

        return ToProto(view);
    }

    public override async Task<ProtoDriver> SuspendDriver(SuspendDriverRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        var view = await dispatcher.SendAsync(
            new SuspendDriverCommand(ParseId(request.DriverId), request.Reason),
            context.CancellationToken).ConfigureAwait(false);

        return ToProto(view);
    }

    public override async Task<ListDriversResponse> ListDrivers(
        ListDriversRequest request,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        var vue = await dispatcher.QueryAsync(
            new ListDriversQuery(
                request.Query,
                ToDomain(request.VerificationStatus),
                ToDomain(request.OperationalStatus),
                request.PageSize == 0 ? 50 : request.PageSize,
                request.Offset),
            context.CancellationToken).ConfigureAwait(false);

        var reponse = new ListDriversResponse { Total = vue.Total };
        reponse.Drivers.AddRange(vue.Drivers.Select(ToProto));

        return reponse;
    }

    public override async Task<ListDriverPositionsResponse> ListDriverPositions(
        ListDriverPositionsRequest request,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        // ZERO VAUT « LE DEFAUT DU SERVICE », comme partout ailleurs dans ce
        // contrat : c'est la valeur par defaut de protobuf, et la traiter
        // comme une demande de zero point rendrait une carte vide.
        var vue = await dispatcher.QueryAsync(
            new ListDriverPositionsQuery(request.Limit == 0 ? 500 : request.Limit),
            context.CancellationToken).ConfigureAwait(false);

        var reponse = new ListDriverPositionsResponse { FreshnessSeconds = vue.FreshnessSeconds };

        reponse.Positions.AddRange(vue.Positions.Select(p => new ProtoDriverPosition
        {
            DriverId = p.DriverId,
            DisplayName = p.DisplayName,
            Latitude = p.Latitude,
            Longitude = p.Longitude,
            SeenAt = Timestamp.FromDateTimeOffset(p.SeenAt),
            OperationalStatus = ToProto(p.OperationalStatus),
            VehicleType = ToProto(p.VehicleType),
        }));

        return reponse;
    }

    public override async Task<DriverStats> GetDriverStats(
        GetDriverStatsRequest request,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        var vue = await dispatcher.QueryAsync(
            new GetDriverStatsQuery(TimeWindowMapper.ToDomain(request.Window, calendrier)),
            context.CancellationToken).ConfigureAwait(false);

        var reponse = new DriverStats
        {
            Window = TimeWindowMapper.ToProto(vue.Window),
            Total = vue.Total,
            RegisteredInWindow = vue.RegisteredInWindow,
            VerifiedInWindow = vue.VerifiedInWindow,
        };

        reponse.ByVerification.AddRange(vue.ByVerification.Select(t => new VerificationStatusCount
        {
            Status = ToProto(t.Status),
            Count = t.Count,
        }));

        reponse.ByOperational.AddRange(vue.ByOperational.Select(t => new OperationalStatusCount
        {
            Status = ToProto(t.Status),
            Count = t.Count,
        }));

        return reponse;
    }

    private static VerificationStatus ToProto(DomainVerification statut) => statut switch
    {
        DomainVerification.PendingVerification => VerificationStatus.PendingVerification,
        DomainVerification.Verified => VerificationStatus.Verified,
        DomainVerification.Rejected => VerificationStatus.Rejected,
        DomainVerification.Suspended => VerificationStatus.Suspended,
        _ => VerificationStatus.Unspecified,
    };

    private static OperationalStatus ToProto(DomainOperational statut) => statut switch
    {
        DomainOperational.Offline => OperationalStatus.Offline,
        DomainOperational.Available => OperationalStatus.Available,
        DomainOperational.Reserved => OperationalStatus.Reserved,
        DomainOperational.OnMission => OperationalStatus.OnMission,
        _ => OperationalStatus.Unspecified,
    };

    private static DomainVerification? ToDomain(VerificationStatus statut) => statut switch
    {
        VerificationStatus.PendingVerification => DomainVerification.PendingVerification,
        VerificationStatus.Verified => DomainVerification.Verified,
        VerificationStatus.Rejected => DomainVerification.Rejected,
        VerificationStatus.Suspended => DomainVerification.Suspended,
        _ => null,
    };

    private static DomainOperational? ToDomain(OperationalStatus statut) => statut switch
    {
        OperationalStatus.Offline => DomainOperational.Offline,
        OperationalStatus.Available => DomainOperational.Available,
        OperationalStatus.Reserved => DomainOperational.Reserved,
        OperationalStatus.OnMission => DomainOperational.OnMission,
        _ => null,
    };

    private static Guid ParseId(string value)
        => Guid.TryParse(value, out var id) && id != Guid.Empty
            ? id
            : throw new DomainException("INVALID_ID", "Le champ driver_id n'est pas un identifiant valide.");

    /// <summary>
    /// Un type non precise veut dire « n'importe lequel », pas « le type zero » :
    /// c'est la valeur par defaut de protobuf, pas un choix de l'appelant.
    /// </summary>
    private static DomainVehicleType? ToDomain(CommonVehicleType type) => type switch
    {
        CommonVehicleType.Motorcycle => DomainVehicleType.Motorcycle,
        CommonVehicleType.Car => DomainVehicleType.Car,
        CommonVehicleType.Van => DomainVehicleType.Van,
        _ => null,
    };

    private static CommonVehicleType ToProto(DomainVehicleType type) => type switch
    {
        DomainVehicleType.Motorcycle => CommonVehicleType.Motorcycle,
        DomainVehicleType.Car => CommonVehicleType.Car,
        DomainVehicleType.Van => CommonVehicleType.Van,
        _ => CommonVehicleType.Unspecified,
    };

    private static DocumentType ToProto(DomainDocumentType type) => type switch
    {
        DomainDocumentType.NationalId => DocumentType.NationalId,
        DomainDocumentType.DrivingLicence => DocumentType.DrivingLicence,
        DomainDocumentType.VehicleRegistration => DocumentType.VehicleRegistration,
        DomainDocumentType.IdentityPhoto => DocumentType.IdentityPhoto,
        DomainDocumentType.VehiclePhoto => DocumentType.VehiclePhoto,
        _ => DocumentType.Unspecified,
    };

    private static ProtoDriver ToProto(DriverView view) => new()
    {
        Id = view.Id.ToString(),
        DisplayName = view.DisplayName,
        Phone = view.Phone,
        Vehicle = new Vehicle
        {
            Type = ToProto(view.VehicleType),
            Plate = view.VehiclePlate,
            CapacityGrams = view.VehicleCapacityGrams,
        },
        VerificationStatus = ToProto(view.VerificationStatus),
        OperationalStatus = ToProto(view.OperationalStatus),
        RegisteredAt = Timestamp.FromDateTimeOffset(view.RegisteredAt),
        VerifiedAt = view.VerifiedAt is null ? null : Timestamp.FromDateTimeOffset(view.VerifiedAt.Value),
        StatusReason = view.StatusReason,
    };
}
