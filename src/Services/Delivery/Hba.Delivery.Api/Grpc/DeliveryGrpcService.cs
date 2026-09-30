using Grpc.Core;
using Hba.BuildingBlocks.Application.Messaging;
using Hba.BuildingBlocks.Domain;
using Hba.BuildingBlocks.Application.Time;
using Hba.BuildingBlocks.Grpc;
using Hba.BuildingBlocks.Grpc.Time;
using Hba.Contracts.Delivery.V1;
using Hba.Delivery.Application.Commands.Closure;
using Hba.Delivery.Application.Commands.CreateDelivery;
using Hba.Delivery.Application.Commands.DriverActions;
using Hba.Delivery.Application.Queries;
using Microsoft.AspNetCore.Authorization;
using ProtoDelivery = Hba.Contracts.Delivery.V1.Delivery;

namespace Hba.Delivery.Api.Grpc;

/// <summary>
/// Entrée synchrone du service. Ne contient aucune règle : elle traduit, appelle
/// la couche Application et retraduit. L'autorisation fine est appliquée dans
/// les handlers, à partir du JWT que CE service a validé.
/// </summary>
[Authorize]
public sealed class DeliveryGrpcService(IDispatcher dispatcher, ITimeCalendar calendrier)
    : DeliveryService.DeliveryServiceBase
{
    public override async Task<CreateDeliveryResponse> CreateDelivery(
        CreateDeliveryRequest request,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        var command = new CreateDeliveryCommand
        {
            IdempotencyKey = string.IsNullOrWhiteSpace(request.IdempotencyKey)
                ? context.RequestHeaders.GetValue(GrpcMetadataKeys.IdempotencyKey)
                : request.IdempotencyKey,
            QuoteId = request.QuoteId,
            Source = DeliveryProtoMapper.FromProtoSource(request.Source),
            ExternalOrderId = Nullify(request.ExternalOrderId),
            MerchantId = Nullify(request.MerchantId),
            PickupPointId = Nullify(request.PickupPointId),
            Pickup = ToInput(request.Pickup, nameof(request.Pickup)),
            Dropoff = ToInput(request.Dropoff, nameof(request.Dropoff)),
            RecipientName = request.RecipientName,
            RecipientPhone = request.RecipientPhone,
            PackageDescription = Nullify(request.PackageDescription),
            PackageWeightGrams = request.PackageWeightGrams,
        };

        var result = await dispatcher.SendAsync(command, context.CancellationToken).ConfigureAwait(false);

        return new CreateDeliveryResponse
        {
            Delivery = DeliveryProtoMapper.ToProto(result.Delivery),
            PaymentIntentId = result.PaymentIntentId ?? string.Empty,
            PaymentRedirectUrl = result.PaymentRedirectUrl ?? string.Empty,
        };
    }

    public override async Task<ProtoDelivery> GetDelivery(GetDeliveryRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        var view = await dispatcher
            .QueryAsync(new GetDeliveryQuery(ParseId(request.DeliveryId)), context.CancellationToken)
            .ConfigureAwait(false);

        return DeliveryProtoMapper.ToProto(view);
    }

    public override async Task<CustomerBilling> GetCustomerBilling(
        GetCustomerBillingRequest request,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        var vue = await dispatcher
            .QueryAsync(new GetCustomerBillingQuery(request.CustomerId), context.CancellationToken)
            .ConfigureAwait(false);

        return new CustomerBilling
        {
            CustomerId = vue.CustomerId,
            DeliveredCount = vue.DeliveredCount,
            BilledTotalXof = vue.BilledTotalXof,
        };
    }

    public override async Task<ListDeliveriesResponse> ListDeliveries(
        ListDeliveriesRequest request,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        var statuses = request.Statuses
            .Select(DeliveryProtoMapper.FromProtoStatus)
            .Where(s => s is not null)
            .Select(s => s!.Value)
            .ToList();

        var offset = int.TryParse(request.PageToken, out var parsed) ? parsed : 0;
        var pageSize = request.PageSize <= 0 ? 25 : request.PageSize;

        var views = await dispatcher
            .QueryAsync(
                new ListDeliveriesQuery(
                    statuses,
                    request.CreatedAfter?.ToDateTimeOffset(),
                    request.CreatedBefore?.ToDateTimeOffset(),
                    pageSize,
                    offset,
                    request.CustomerId),
                context.CancellationToken)
            .ConfigureAwait(false);

        var response = new ListDeliveriesResponse
        {
            NextPageToken = views.Count < pageSize ? string.Empty : (offset + pageSize).ToString(),
        };

        response.Deliveries.AddRange(views.Select(DeliveryProtoMapper.ToProto));

        return response;
    }

    public override async Task<ProtoDelivery> CancelDelivery(CancelDeliveryRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        var view = await dispatcher
            .SendAsync(new CancelDeliveryCommand(ParseId(request.DeliveryId), request.Reason), context.CancellationToken)
            .ConfigureAwait(false);

        return DeliveryProtoMapper.ToProto(view);
    }

    public override async Task<ProtoDelivery> MarkArrivedAtPickup(
        MarkArrivedAtPickupRequest request,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        var view = await dispatcher
            .SendAsync(
                new MarkArrivedAtPickupCommand(
                    ParseId(request.DeliveryId),
                    request.OccurredAt?.ToDateTimeOffset(),
                    Nullify(request.IdempotencyKey)),
                context.CancellationToken)
            .ConfigureAwait(false);

        return DeliveryProtoMapper.ToProto(view);
    }

    public override async Task<ProtoDelivery> MarkPickedUp(MarkPickedUpRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        var view = await dispatcher
            .SendAsync(
                new MarkPickedUpCommand(
                    ParseId(request.DeliveryId),
                    request.OccurredAt?.ToDateTimeOffset(),
                    Nullify(request.IdempotencyKey),
                    Nullify(request.ProofObjectKey)),
                context.CancellationToken)
            .ConfigureAwait(false);

        return DeliveryProtoMapper.ToProto(view);
    }

    public override async Task<ProtoDelivery> ConfirmDelivery(ConfirmDeliveryRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        var view = await dispatcher
            .SendAsync(
                new ConfirmDeliveryCommand(
                    ParseId(request.DeliveryId),
                    request.Otp,
                    request.OccurredAt?.ToDateTimeOffset(),
                    Nullify(request.IdempotencyKey),
                    Nullify(request.ProofObjectKey)),
                context.CancellationToken)
            .ConfigureAwait(false);

        return DeliveryProtoMapper.ToProto(view);
    }

    public override async Task<ProtoDelivery> DeclareIncident(
        DeclareIncidentRequest request,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        var view = await dispatcher
            .SendAsync(
                new DeclareIncidentCommand(
                    ParseId(request.DeliveryId),
                    request.Reason,
                    request.OccurredAt?.ToDateTimeOffset(),
                    Nullify(request.IdempotencyKey)),
                context.CancellationToken)
            .ConfigureAwait(false);

        return DeliveryProtoMapper.ToProto(view);
    }

    public override async Task<ProtoDelivery> AdminCloseDelivery(
        AdminCloseDeliveryRequest request,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        var target = DeliveryProtoMapper.FromProtoStatus(request.TargetStatus)
            ?? throw new DomainException("INVALID_TARGET_STATUS", "Statut de clôture inconnu.");

        var view = await dispatcher
            .SendAsync(
                new AdminCloseDeliveryCommand(ParseId(request.DeliveryId), target, request.Reason),
                context.CancellationToken)
            .ConfigureAwait(false);

        return DeliveryProtoMapper.ToProto(view);
    }

    public override async Task<DeliveryStats> GetDeliveryStats(
        GetDeliveryStatsRequest request,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        var granularite = TimeWindowMapper.ToDomain(request.Granularity);
        var fenetre = TimeWindowMapper.ToDomain(request.Window, calendrier);

        var vue = await dispatcher.QueryAsync(
            new GetDeliveryStatsQuery(fenetre, granularite),
            context.CancellationToken).ConfigureAwait(false);

        var total = vue.ByStatus.Sum(t => t.Count);
        var livrees = vue.ByStatus
            .Where(t => t.Status == Domain.Deliveries.DeliveryStatus.Delivered)
            .Sum(t => t.Count);
        var closes = vue.ByStatus
            .Where(t => Domain.Deliveries.DeliveryTransitions.Terminal.Contains(t.Status))
            .Sum(t => t.Count);

        var reponse = new DeliveryStats
        {
            Window = TimeWindowMapper.ToProto(vue.Window),
            Total = total,
            Open = total - closes,
            Delivered = livrees,
            Closed = closes,
            BilledXof = vue.ByStatus.Sum(t => t.BilledXof),
            AvgSecondsToAssignment = vue.ToAssignment.AverageSeconds,
            AssignmentSamples = vue.ToAssignment.Samples,
            AvgSecondsToPickup = vue.ToPickup.AverageSeconds,
            PickupSamples = vue.ToPickup.Samples,
            AvgSecondsToDelivery = vue.ToDelivery.AverageSeconds,
            DeliverySamples = vue.ToDelivery.Samples,
        };

        reponse.ByStatus.AddRange(vue.ByStatus.Select(t => new StatusCount
        {
            Status = DeliveryProtoMapper.ToProtoStatus(t.Status),
            Count = t.Count,
        }));

        reponse.BySource.AddRange(vue.BySource.Select(t => new SourceCount
        {
            Source = DeliveryProtoMapper.ToProtoSource(t.Source),
            Count = t.Count,
        }));

        reponse.CreatedSeries.AddRange(vue.CreatedSeries.Select(p => new Contracts.Common.V1.SeriesPoint
        {
            Key = p.Key,
            Value = p.Count,
        }));

        return reponse;
    }

    private static Guid ParseId(string value)
        => Guid.TryParse(value, out var id)
            ? id
            : throw new DomainException("INVALID_ID", $"Identifiant de livraison invalide : {value}.");

    private static string? Nullify(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static LocationInput ToInput(Contracts.Common.V1.Location? location, string field)
    {
        if (location?.Point is null)
        {
            throw new DomainException("MISSING_LOCATION", $"{field} est obligatoire, avec son point GPS.");
        }

        return new LocationInput(
            location.Point.Latitude,
            location.Point.Longitude,
            location.Landmark,
            location.Phone,
            location.ContactName,
            Nullify(location.Notes));
    }
}
