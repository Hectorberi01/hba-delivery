using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Hba.BuildingBlocks.Application.Messaging;
using Hba.Contracts.Ride.V1;
using Hba.Ride.Application.Common.Views;
using Hba.Ride.Application.Features.Rides.Commands;
using Hba.Ride.Application.Features.Rides.Queries;
using DomainMoney = Hba.Ride.Domain.ValueObjects.MoneyXof;
using DomainStatus = Hba.Ride.Domain.Rides.RideStatus;
using DomainVehicle = Hba.Ride.Domain.Rides.VehicleType;
using ProtoRide = Hba.Contracts.Ride.V1.Ride;

namespace Hba.Ride.Api.Grpc;

/// <summary>
/// La frontière gRPC du service Ride.
/// </summary>
///
/// <remarks>
/// ELLE NE DÉCIDE RIEN. Aucune règle d'autorisation, aucune transition, aucun
/// prix : elle traduit un message en commande, une vue en message, et un refus en
/// statut gRPC. Tout ce qui ressemble à une décision ici serait une règle que les
/// tests de la couche Application ne couvriraient pas.
///
/// AUCUNE REQUÊTE NE PORTE L'IDENTITÉ DE L'APPELANT, et c'est le contrat qui le
/// dit. Elle est lue dans le jeton que ce service a validé lui-même (ADR 0007) :
/// un <c>customer_id</c> dans un message serait la porte par laquelle un
/// téléphone commanderait au nom d'un autre.
/// </remarks>
internal sealed class RideGrpcService(IDispatcher dispatcher) : RideService.RideServiceBase
{
    public override async Task<RequestRideResponse> RequestRide(
        RequestRideRequest request,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);

        var vue = await dispatcher.SendAsync(
            new RequestRideCommand(
                request.QuoteId,
                request.Pickup?.Latitude ?? 0,
                request.Pickup?.Longitude ?? 0,
                request.Destination?.Latitude ?? 0,
                request.Destination?.Longitude ?? 0,
                Vehicule(request.VehicleType),
                request.IdempotencyKey,
                request.PayerPhone),
            context.CancellationToken).ConfigureAwait(false);

        var demandee = vue.Exiger();

        return new RequestRideResponse
        {
            Ride = Proto(demandee.Ride),
            // VIDE QUAND LE PAIEMENT N'A PAS PU S'OUVRIR, et la course existe quand
            // meme : le devis etait deja consomme. C'est a l'application de le dire,
            // pas d'afficher un bouton qui ne mene nulle part.
            PaymentIntentId = demandee.PaymentIntentId,
            PaymentRedirectUrl = demandee.PaymentRedirectUrl,
        };
    }

    public override async Task<ProtoRide> GetRide(GetRideRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);

        var vue = await dispatcher.QueryAsync(
            new GetRideByIdQuery(Identifiant(request.RideId)),
            context.CancellationToken).ConfigureAwait(false);

        return Proto(vue.Exiger());
    }

    public override async Task<ActiveRides> GetMyActiveRides(
        GetMyActiveRidesRequest request,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var vues = await dispatcher.QueryAsync(
            new GetCustomerActiveRidesQuery(),
            context.CancellationToken).ConfigureAwait(false);

        var reponse = new ActiveRides();
        reponse.Rides.AddRange(vues.Exiger().Select(Proto));

        return reponse;
    }

    public override async Task<ListMyRidesResponse> ListMyRides(
        ListMyRidesRequest request,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);

        // ZÉRO VEUT DIRE « LA TAILLE PAR DÉFAUT », pas « aucune ligne ».
        // Protobuf ne distingue pas un entier absent d'un entier nul : prendre
        // zéro au mot rendrait une page vide à tout appelant qui n'a rien
        // précisé. La borne haute, elle, est posée dans le gestionnaire.
        var taille = request.PageSize <= 0 ? 20 : request.PageSize;

        var page = await dispatcher.QueryAsync(
            new GetCustomerRideHistoryQuery(taille, request.Offset),
            context.CancellationToken).ConfigureAwait(false);

        var vue = page.Exiger();
        var reponse = new ListMyRidesResponse { Total = vue.Total };
        reponse.Rides.AddRange(vue.Rides.Select(Proto));

        return reponse;
    }

    public override async Task<ActiveRide> GetMyDriverRide(
        GetMyDriverRideRequest request,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var vue = await dispatcher.QueryAsync(
            new GetDriverActiveRideQuery(),
            context.CancellationToken).ConfigureAwait(false);

        var course = vue.Exiger();

        // LE DRAPEAU EXISTE PARCE QU'UN MESSAGE PROTO ABSENT ET UN MESSAGE AUX
        // CHAMPS PAR DÉFAUT SONT INDISTINGUABLES côté client. Sans lui, « aucune
        // course » arriverait comme une course d'identifiant vide.
        return course is null
            ? new ActiveRide { Present = false }
            : new ActiveRide { Present = true, Ride = Proto(course) };
    }

    public override Task<ProtoRide> DeclareOnTheWay(DeclareOnTheWayRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);

        return Appliquer(new DriverIsOnTheWayCommand(Identifiant(request.RideId)), context);
    }

    public override Task<ProtoRide> DeclareArrived(DeclareArrivedRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);

        return Appliquer(new DriverHasArrivedCommand(Identifiant(request.RideId)), context);
    }

    public override Task<ProtoRide> StartRide(StartRideRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);

        return Appliquer(new StartRideCommand(Identifiant(request.RideId)), context);
    }

    public override Task<ProtoRide> CompleteRide(CompleteRideRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);

        return Appliquer(new CompleteRideCommand(Identifiant(request.RideId)), context);
    }

    public override Task<ProtoRide> CancelRide(CancelRideRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);

        return Appliquer(
            new CancelRideCommand(
                Identifiant(request.RideId),
                string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason),
            context);
    }

    private async Task<ProtoRide> Appliquer(
        ICommand<Hba.Ride.Domain.Results.Result<RideView>> commande,
        ServerCallContext context)
    {
        var vue = await dispatcher.SendAsync(commande, context.CancellationToken).ConfigureAwait(false);

        return Proto(vue.Exiger());
    }

    /// <summary>
    /// UN IDENTIFIANT ILLISIBLE EST UN ARGUMENT INVALIDE, PAS UNE COURSE
    /// INTROUVABLE. <c>Guid.Empty</c> partirait chercher en base une clé qui
    /// n'existe nulle part et rendrait 404, ce qui laisserait croire à un appelant
    /// que sa chaîne était bien formée.
    /// </summary>
    private static Guid Identifiant(string value)
        => Guid.TryParse(value, out var id)
            ? id
            : throw new RpcException(new Status(
                StatusCode.InvalidArgument,
                "L'identifiant de course n'est pas un UUID."));

    private static ProtoRide Proto(RideView vue)
    {
        var course = new ProtoRide
        {
            Id = vue.Id.ToString(),
            CustomerId = vue.CustomerId,
            DriverId = vue.DriverId ?? string.Empty,
            Pickup = Point(vue.PickupLatitude, vue.PickupLongitude),
            Destination = Point(vue.DestinationLatitude, vue.DestinationLongitude),
            VehicleType = Vehicule(vue.VehicleType),
            Status = Etat(vue.Status),
            EstimatedDistanceMeters = vue.EstimatedDistanceMeters,
            EstimatedDurationSeconds = vue.EstimatedDurationSeconds,
            EstimatedPrice = Montant(vue.EstimatedPrice),
            RequestedAt = Timestamp.FromDateTimeOffset(vue.RequestedAt),
            CancellationReason = vue.CancellationReason ?? string.Empty,
            PaymentIntentId = vue.PaymentIntentId ?? string.Empty,

            // POSE SANS CONDITION, CONTRAIREMENT AUX INSTANTS CI-DESSOUS. Zero est
            // une reponse — « annulation gratuite » — et non une absence : un champ
            // laisse vide se lirait « on ne sait pas », ce qui est l'inverse.
            CancellationFeeDue = Montant(vue.CancellationFeeDue),
        };

        if (vue.Driver is { } chauffeur)
        {
            course.Driver = new Contracts.Ride.V1.AssignedDriver
            {
                DriverId = chauffeur.DriverId,
                DisplayName = chauffeur.DisplayName,
                Phone = chauffeur.Phone,
                VehicleType = Vehicule(chauffeur.VehicleType),
                VehiclePlate = chauffeur.VehiclePlate,
                VehicleLabel = chauffeur.Vehicule,
            };
        }

        // LES INSTANTS FACULTATIFS NE SE POSENT QUE S'ILS EXISTENT. Un Timestamp
        // par défaut vaut le 1er janvier 1970, et une application cliente y lirait
        // une course affectée à cette date.
        if (vue.FinalPrice is not null) { course.FinalPrice = Montant(vue.FinalPrice.Value); }
        if (vue.DriverAssignedAt is not null) { course.DriverAssignedAt = Timestamp.FromDateTimeOffset(vue.DriverAssignedAt.Value); }
        if (vue.DriverArrivedAt is not null) { course.DriverArrivedAt = Timestamp.FromDateTimeOffset(vue.DriverArrivedAt.Value); }
        if (vue.StartedAt is not null) { course.StartedAt = Timestamp.FromDateTimeOffset(vue.StartedAt.Value); }
        if (vue.CompletedAt is not null) { course.CompletedAt = Timestamp.FromDateTimeOffset(vue.CompletedAt.Value); }
        if (vue.CancelledAt is not null) { course.CancelledAt = Timestamp.FromDateTimeOffset(vue.CancelledAt.Value); }

        return course;
    }

    private static Contracts.Common.V1.GeoPoint Point(double latitude, double longitude)
        => new() { Latitude = latitude, Longitude = longitude };

    private static Contracts.Common.V1.Money Montant(long amount)
        => new() { Amount = amount, Currency = DomainMoney.CurrencyCode };

    private static DomainVehicle Vehicule(RideVehicleType type) => type switch
    {
        RideVehicleType.Motorcycle => DomainVehicle.Motorcycle,
        RideVehicleType.Car => DomainVehicle.Car,
        RideVehicleType.Tricycle => DomainVehicle.Tricycle,
        _ => DomainVehicle.Unspecified,
    };

    private static RideVehicleType Vehicule(DomainVehicle type) => type switch
    {
        DomainVehicle.Motorcycle => RideVehicleType.Motorcycle,
        DomainVehicle.Car => RideVehicleType.Car,
        DomainVehicle.Tricycle => RideVehicleType.Tricycle,
        _ => RideVehicleType.Unspecified,
    };

    private static Contracts.Ride.V1.RideStatus Etat(DomainStatus status) => status switch
    {
        DomainStatus.Requested => Contracts.Ride.V1.RideStatus.Requested,
        DomainStatus.SearchingDriver => Contracts.Ride.V1.RideStatus.SearchingDriver,
        DomainStatus.DriverAssigned => Contracts.Ride.V1.RideStatus.DriverAssigned,
        DomainStatus.DriverArriving => Contracts.Ride.V1.RideStatus.DriverArriving,
        DomainStatus.DriverArrived => Contracts.Ride.V1.RideStatus.DriverArrived,
        DomainStatus.InProgress => Contracts.Ride.V1.RideStatus.InProgress,
        DomainStatus.Completed => Contracts.Ride.V1.RideStatus.Completed,
        DomainStatus.Cancelled => Contracts.Ride.V1.RideStatus.Cancelled,
        DomainStatus.NoDriverAvailable => Contracts.Ride.V1.RideStatus.NoDriverAvailable,
        DomainStatus.PaymentFailed => Contracts.Ride.V1.RideStatus.PaymentFailed,
        _ => Contracts.Ride.V1.RideStatus.Unspecified,
    };
}
