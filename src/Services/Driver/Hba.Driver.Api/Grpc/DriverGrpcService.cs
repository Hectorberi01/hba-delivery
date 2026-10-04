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
using DomainCapability = Hba.Driver.Domain.Drivers.DriverCapability;
using DomainDocumentType = Hba.Driver.Domain.Drivers.DocumentType;
using ProtoCapability = Hba.Contracts.Common.V1.DriverCapability;
using DomainOperational = Hba.Driver.Domain.Drivers.OperationalStatus;
using DomainVehicleType = Hba.Driver.Domain.Drivers.VehicleType;
using DomainVerification = Hba.Driver.Domain.Drivers.VerificationStatus;
using ProtoDriver = Hba.Contracts.Driver.V1.Driver;

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
            VehicleBrand = view.VehicleBrand,
            VehicleModel = view.VehicleModel,
            VehicleColor = view.VehicleColor,
        };
    }

    /// <summary>
    /// Passage en ligne. LA POSITION DU CONTRAT EST IGNOREE ICI.
    /// </summary>
    ///
    /// <remarks>
    /// LE CHAMP RESTE AU CONTRAT ET N'EST PLUS LU, et c'est un choix de transition.
    /// Driver n'ecrit plus de position depuis le 1er octobre 2026 : c'est la passerelle
    /// qui enchaine Driver pour l'etat et Location pour le premier point. Mais une
    /// application chauffeur deja installee envoie encore sa position dans ce message,
    /// et refuser un passage en ligne qui la porte n'aurait rien ameliore.
    ///
    /// LE CHAMP SERA RETIRE QUAND LES APPLICATIONS DEPLOYEES NE L'ENVERRONT PLUS, et son
    /// numero sera alors reserve. En attendant, il est documente comme ignore — un champ
    /// silencieusement jete est exactement ce qui a fait chiffrer les colis de trente
    /// kilos au prix d'une enveloppe pendant des mois.
    /// </remarks>
    public override async Task<ProtoDriver> GoOnline(GoOnlineRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        var view = await dispatcher.SendAsync(
            new GoOnlineCommand(ParseId(request.DriverId)),
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

    public override async Task<DriverAvailability> CheckDriverAvailability(
        CheckDriverAvailabilityRequest request,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        var vue = await dispatcher.QueryAsync(
            new CheckDriverAvailabilityQuery(ParseId(request.DriverId)),
            context.CancellationToken).ConfigureAwait(false);

        return new DriverAvailability
        {
            DriverId = vue.DriverId.ToString(),
            DisplayName = vue.DisplayName,
            Offerable = vue.Offerable,
            Reason = vue.Reason ?? string.Empty,
            VerificationStatus = ToProto(vue.VerificationStatus),
            OperationalStatus = ToProto(vue.OperationalStatus),
        };
    }

    /// <summary>
    /// Le versant « qui » du croisement : parmi ces identifiants, lesquels sont
    /// offrables.
    /// </summary>
    ///
    /// <remarks>
    /// AUCUNE COORDONNEE N'ENTRE NI NE SORT. C'est a cela qu'on verifie, en lisant ce
    /// fichier, que la frontiere posee le 1er octobre 2026 tient : si une latitude
    /// reapparait dans ce service, c'est que le croisement est revenu ici.
    /// </remarks>
    public override async Task<FilterAvailableDriversResponse> FilterAvailableDrivers(
        FilterAvailableDriversRequest request,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        var vues = await dispatcher.QueryAsync(
            new FilterAvailableDriversQuery(
                Identifiants(request.DriverIds),
                ToDomain(request.VehicleType),
                ToDomain(request.RequiredCapability)),
            context.CancellationToken).ConfigureAwait(false);

        var reponse = new FilterAvailableDriversResponse();

        reponse.Drivers.AddRange(vues.Select(v => new EligibleDriver
        {
            DriverId = v.DriverId.ToString(),
            VehicleType = ToProto(v.VehicleType),
        }));

        return reponse;
    }

    /// <summary>
    /// Les pastilles de la carte du client.
    /// </summary>
    ///
    /// <remarks>
    /// AUCUNE COORDONNEE N'ENTRE NI NE SORT, comme pour le filtre juste au-dessus :
    /// Location tient les positions, ce service tient les livreurs, et la passerelle
    /// recolle les deux.
    /// </remarks>
    public override async Task<GetDriverPinsResponse> GetDriverPins(
        GetDriverPinsRequest request,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        var vues = await dispatcher.QueryAsync(
            new GetDriverPinsQuery(Identifiants(request.DriverIds)),
            context.CancellationToken).ConfigureAwait(false);

        var reponse = new GetDriverPinsResponse();

        reponse.Pins.AddRange(vues.Select(v => new DriverPin
        {
            DriverId = v.DriverId.ToString(),
            VehicleType = ToProto(v.VehicleType),
            Available = v.Available,
        }));

        return reponse;
    }

    public override async Task<GetDriverSummariesResponse> GetDriverSummaries(
        GetDriverSummariesRequest request,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        var vues = await dispatcher.QueryAsync(
            new GetDriverSummariesQuery(Identifiants(request.DriverIds)),
            context.CancellationToken).ConfigureAwait(false);

        var reponse = new GetDriverSummariesResponse();

        reponse.Drivers.AddRange(vues.Select(v => new DriverSummary
        {
            DriverId = v.DriverId.ToString(),
            DisplayName = v.DisplayName,
            VerificationStatus = ToProto(v.VerificationStatus),
            OperationalStatus = ToProto(v.OperationalStatus),
            VehicleType = ToProto(v.VehicleType),
        }));

        return reponse;
    }

    /// <summary>
    /// Traduit une liste d'identifiants, en ignorant les illisibles.
    /// </summary>
    ///
    /// <remarks>
    /// ON IGNORE AU LIEU DE REFUSER, ET C'EST LE CONTRAIRE DE <c>ParseId</c>. La
    /// difference tient a ce que l'appel demande : un identifiant unique mal forme rend
    /// la question insensee, donc on refuse ; une LISTE dont un element est illisible
    /// garde un sens pour tous les autres. Et cette liste vient d'un index Redis, pas
    /// d'un humain : faire echouer une vague entiere, ou une carte entiere, pour une
    /// entree abimee serait une panne la ou il suffit de sauter une ligne.
    /// </remarks>
    private static IReadOnlyCollection<Guid> Identifiants(IEnumerable<string> valeurs)
        =>
        [
            .. valeurs
                .Select(id => Guid.TryParse(id, out var identifiant) ? identifiant : Guid.Empty)
                .Where(id => id != Guid.Empty),
        ];

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
                request.CapacityGrams,
                request.Brand,
                request.Model,
                request.Color,
                request.PassengerCapacity),
            context.CancellationToken).ConfigureAwait(false);

        return ToProto(view);
    }

    public override async Task<ProtoDriver> DeclareCapabilities(
        DeclareCapabilitiesRequest request,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        // PAS DE REPLI SUR LE COLIS ICI, CONTRAIREMENT A LA RECHERCHE. Une
        // valeur non reconnue dans une DECLARATION serait silencieusement
        // traduite en « colis », et le livreur croirait avoir declare autre
        // chose. L'agregat ecarte Unspecified ; ce qui arrive ici doit donc
        // etre nomme.
        var demandees = request.Capabilities
            .Where(c => c != ProtoCapability.Unspecified)
            .Select(c => c switch
            {
                ProtoCapability.PackageDelivery => DomainCapability.PackageDelivery,
                ProtoCapability.PassengerTransport => DomainCapability.PassengerTransport,
                ProtoCapability.FoodDelivery => DomainCapability.FoodDelivery,
                _ => throw new RpcException(new Status(
                    StatusCode.InvalidArgument,
                    "Habilitation inconnue.")),
            })
            .ToList();

        var view = await dispatcher.SendAsync(
            new DeclareCapabilitiesCommand(ParseId(request.DriverId), demandees),
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
                PassengerCapacity = view.VehiclePassengerCapacity,
            },
            ProfilePhotoUrl = view.ProfilePhotoUrl?.ToString() ?? string.Empty,
            CanSubmit = view.CanSubmit,
            VehicleDeclared = view.VehicleDeclared,
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
        reponse.RequiredDocuments.AddRange(view.RequiredDocuments.Select(ToProto));
        reponse.Capabilities.AddRange(view.Capabilities.Select(ToProto));
        reponse.AllowedCapabilities.AddRange(view.AllowedCapabilities.Select(ToProto));

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

    public override async Task<ProtoDriver> EnsureDriver(EnsureDriverRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        var view = await dispatcher.SendAsync(
            new EnsureDriverCommand(),
            context.CancellationToken).ConfigureAwait(false);

        return ToProto(view);
    }

    public override async Task<ProtoDriver> ReinstateDriver(ReinstateDriverRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        var view = await dispatcher.SendAsync(
            new ReinstateDriverCommand(ParseId(request.DriverId), request.Reason),
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
        CommonVehicleType.Bicycle => DomainVehicleType.Bicycle,
        CommonVehicleType.Tricycle => DomainVehicleType.Tricycle,
        _ => null,
    };

    private static CommonVehicleType ToProto(DomainVehicleType type) => type switch
    {
        DomainVehicleType.Motorcycle => CommonVehicleType.Motorcycle,
        DomainVehicleType.Car => CommonVehicleType.Car,
        DomainVehicleType.Van => CommonVehicleType.Van,
        DomainVehicleType.Bicycle => CommonVehicleType.Bicycle,
        DomainVehicleType.Tricycle => CommonVehicleType.Tricycle,
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
            Brand = view.VehicleBrand,
            Model = view.VehicleModel,
            Color = view.VehicleColor,
            PassengerCapacity = view.VehiclePassengerCapacity,
        },
        VerificationStatus = ToProto(view.VerificationStatus),
        OperationalStatus = ToProto(view.OperationalStatus),
        RegisteredAt = Timestamp.FromDateTimeOffset(view.RegisteredAt),
        VerifiedAt = view.VerifiedAt is null ? null : Timestamp.FromDateTimeOffset(view.VerifiedAt.Value),
        StatusReason = view.StatusReason,
        Capabilities = { view.Capabilities.Select(ToProto) },
    };

    // ---- Habilitations --------------------------------------------------------

    /// <summary>
    /// L'habilitation exigee par une recherche.
    /// </summary>
    ///
    /// <remarks>
    /// NON PRECISEE VAUT « LIVRAISON DE COLIS », et ce repli est ce qui rend le
    /// champ retrocompatible : le dispatch d'aujourd'hui ne le renseigne pas, et
    /// doit continuer de recevoir exactement les memes livreurs qu'avant.
    ///
    /// C'EST L'INVERSE DU CHOIX FAIT POUR UNE ECRITURE. Ecrire une habilitation
    /// sans la nommer serait absurde ; en CHERCHER une sans la nommer a un sens
    /// evident, puisqu'un seul metier existait avant.
    /// </remarks>
    private static DomainCapability ToDomain(ProtoCapability capability) => capability switch
    {
        ProtoCapability.PassengerTransport => DomainCapability.PassengerTransport,
        ProtoCapability.FoodDelivery => DomainCapability.FoodDelivery,
        _ => DomainCapability.PackageDelivery,
    };

    private static ProtoCapability ToProto(DomainCapability capability) => capability switch
    {
        DomainCapability.PackageDelivery => ProtoCapability.PackageDelivery,
        DomainCapability.PassengerTransport => ProtoCapability.PassengerTransport,
        DomainCapability.FoodDelivery => ProtoCapability.FoodDelivery,
        _ => ProtoCapability.Unspecified,
    };
}
