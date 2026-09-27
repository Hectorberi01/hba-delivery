using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Hba.BuildingBlocks.Application.Messaging;
using Hba.BuildingBlocks.Application.Time;
using Hba.BuildingBlocks.Domain;
using Hba.BuildingBlocks.Grpc.Time;
using Hba.Contracts.Dispatch.V1;
using Hba.Dispatch.Application.Common.Views;
using Hba.Dispatch.Application.Features.Dispatching.Commands;
using Hba.Dispatch.Application.Features.Dispatching.Queries;
using Microsoft.AspNetCore.Authorization;
using DomainDispatchStatus = Hba.Dispatch.Domain.Dispatching.DispatchStatus;
using DomainOfferStatus = Hba.Dispatch.Domain.Dispatching.OfferStatus;

namespace Hba.Dispatch.Api.Grpc;

[Authorize]
public sealed class DispatchGrpcService(IDispatcher dispatcher, ITimeCalendar calendrier)
    : DispatchService.DispatchServiceBase
{
    public override async Task<AcceptOfferResponse> AcceptOffer(
        AcceptOfferRequest request,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        var result = await dispatcher.SendAsync(
            new AcceptOfferCommand(
                ParseId(request.OfferId, "offer_id"),
                string.IsNullOrWhiteSpace(request.IdempotencyKey) ? null : request.IdempotencyKey),
            context.CancellationToken).ConfigureAwait(false);

        var response = new AcceptOfferResponse
        {
            Accepted = result.Accepted,
            RejectionCode = result.RejectionCode ?? string.Empty,
        };

        if (result.Offer is not null)
        {
            response.Offer = ToProto(result.Offer);
        }

        return response;
    }

    public override async Task<DeclineOfferResponse> DeclineOffer(
        DeclineOfferRequest request,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        var ok = await dispatcher.SendAsync(
            new DeclineOfferCommand(ParseId(request.OfferId, "offer_id"), request.Reason),
            context.CancellationToken).ConfigureAwait(false);

        return new DeclineOfferResponse { Ok = ok };
    }

    public override async Task<Offer> GetCurrentOffer(GetCurrentOfferRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        var vue = await dispatcher.QueryAsync(
            new GetCurrentOfferQuery(request.DriverId),
            context.CancellationToken).ConfigureAwait(false);

        // UNE OFFRE VIDE PLUTOT QU'UNE ERREUR : « je n'ai pas d'offre en
        // cours » est la reponse normale d'un livreur en ligne qui attend.
        // Un NOT_FOUND obligerait l'application a traiter un cas ordinaire
        // comme une panne.
        return vue is null ? new Offer() : ToProto(vue);
    }

    // ForceReassign n'est volontairement pas implemente : il renvoie
    // UNIMPLEMENTED. Reaffecter une course deja acceptee touche a la politique
    // d'annulation — le livreur deja en route est-il dedommage de son
    // deplacement, et sur quelle base ? C'est le point 3 des points a
    // trancher, et il n'est pas tranche.
    //
    // OfferToDriver, LUI, EST IMPLEMENTE, et la difference tient en une
    // phrase : il n'enleve rien a personne. La course cherche encore preneur ;
    // l'exploitation designe quelqu'un au lieu d'attendre la vague suivante,
    // et ce quelqu'un reste libre de refuser.

    public override async Task<OfferToDriverResponse> OfferToDriver(
        OfferToDriverRequest request,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        var result = await dispatcher.SendAsync(
            new OfferToDriverCommand(
                ParseId(request.DeliveryId, "delivery_id"),
                request.DriverId,
                string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason),
            context.CancellationToken).ConfigureAwait(false);

        var response = new OfferToDriverResponse
        {
            Sent = result.Sent,
            RejectionCode = result.RejectionCode ?? string.Empty,
        };

        if (result.Offer is not null)
        {
            response.Offer = ToProto(result.Offer);
        }

        return response;
    }

    public override async Task<DispatchStats> GetDispatchStats(
        GetDispatchStatsRequest request,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        var vue = await dispatcher.QueryAsync(
            new GetDispatchStatsQuery(TimeWindowMapper.ToDomain(request.Window, calendrier)),
            context.CancellationToken).ConfigureAwait(false);

        long Recherches(DomainDispatchStatus statut)
            => vue.ByStatus.Where(t => t.Status == statut).Sum(t => t.Count);

        // COMPTEURS NOMMES PLUTOT QU'UNE ENUMERATION : DispatchStatus n'est
        // pas dans le contrat, et cette lecture n'est pas une raison de l'y
        // faire entrer. La machine a etats publique reste celle de la
        // livraison.
        var reponse = new DispatchStats
        {
            Window = TimeWindowMapper.ToProto(vue.Window),
            Dispatches = vue.ByStatus.Sum(t => t.Count),
            Assigned = Recherches(DomainDispatchStatus.Assigned),
            Exhausted = Recherches(DomainDispatchStatus.Exhausted),
            Cancelled = Recherches(DomainDispatchStatus.Cancelled),
            StillSearching = Recherches(DomainDispatchStatus.Searching),
            OffersSent = vue.OffersByStatus.Sum(t => t.Count),
            AvgSecondsToAssignment = vue.ToAssignment.AverageSeconds,
            AssignmentSamples = vue.ToAssignment.Samples,
            AvgSecondsToOfferResponse = vue.ToOfferResponse.AverageSeconds,
            OfferResponseSamples = vue.ToOfferResponse.Samples,
        };

        reponse.OffersByStatus.AddRange(vue.OffersByStatus.Select(t => new OfferStatusCount
        {
            Status = ToProto(t.Status),
            Count = t.Count,
        }));

        reponse.AcceptedByWave.AddRange(vue.AcceptedByWave.Select(t => new WaveCount
        {
            WaveNumber = t.WaveNumber,
            Count = t.Count,
        }));

        return reponse;
    }

    private static OfferStatus ToProto(DomainOfferStatus statut) => statut switch
    {
        DomainOfferStatus.Pending => OfferStatus.Pending,
        DomainOfferStatus.Accepted => OfferStatus.Accepted,
        DomainOfferStatus.Declined => OfferStatus.Declined,
        DomainOfferStatus.Expired => OfferStatus.Expired,
        DomainOfferStatus.Superseded => OfferStatus.Superseded,
        _ => OfferStatus.Unspecified,
    };

    private static Guid ParseId(string value, string field)
        => Guid.TryParse(value, out var id) && id != Guid.Empty
            ? id
            : throw new DomainException("INVALID_ID", $"Le champ {field} n'est pas un identifiant valide.");

    private static Offer ToProto(OfferView view) => new()
    {
        Id = view.Id.ToString(),
        DeliveryId = view.DeliveryId.ToString(),
        DriverId = view.DriverId,
        Status = ToProto(view.Status),
        WaveNumber = view.WaveNumber,
        SentAt = Timestamp.FromDateTimeOffset(view.SentAt),
        ExpiresAt = Timestamp.FromDateTimeOffset(view.ExpiresAt),
        PickupLandmark = view.PickupLandmark,
        DistanceToPickupMeters = view.DistanceToPickupMeters,
        TripDistanceMeters = view.TripDistanceMeters,
        DriverEarningXof = view.DriverEarningXof,

        // LE POINT DE COLLECTE SEUL. La destination n'est pas dans la vue,
        // donc elle ne peut pas fuir ici par distraction.
        Pickup = new Hba.Contracts.Common.V1.GeoPoint
        {
            Latitude = view.PickupLatitude,
            Longitude = view.PickupLongitude,
        },
    };
}
