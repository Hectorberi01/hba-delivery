using Grpc.Core;
using Hba.BuildingBlocks.Application.Messaging;
using Hba.BuildingBlocks.Domain;
using Hba.BuildingBlocks.Grpc;
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
public sealed class DeliveryGrpcService(IDispatcher dispatcher) : DeliveryService.DeliveryServiceBase
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
                    offset),
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
