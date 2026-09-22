using Grpc.Core;
using Hba.BuildingBlocks.Application.Messaging;
using Hba.BuildingBlocks.Domain;
using Hba.Contracts.Directory.V1;
using Hba.Directory.Application.Customers;
using Hba.Directory.Application.Merchants;
using Microsoft.AspNetCore.Authorization;
using ProtoCustomer = Hba.Contracts.Directory.V1.Customer;
using ProtoMerchant = Hba.Contracts.Directory.V1.Merchant;
using ProtoPickupPoint = Hba.Contracts.Directory.V1.PickupPoint;

namespace Hba.Directory.Api.Grpc;

/// <summary>
/// Entrée synchrone de Directory. Aucune méthode n'est anonyme : consulter un
/// profil ou une adresse suppose toujours de savoir qui demande.
/// </summary>
[Authorize]
public sealed class DirectoryGrpcService(IDispatcher dispatcher) : DirectoryService.DirectoryServiceBase
{
    // ---------------------------------------------------------- Client ---

    public override async Task<ProtoCustomer> GetCustomer(GetCustomerRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        var view = await dispatcher.QueryAsync(
            new GetCustomerQuery(Nullify(request.CustomerId)),
            context.CancellationToken).ConfigureAwait(false);

        return DirectoryProtoMapper.ToProto(view);
    }

    public override async Task<ProtoCustomer> UpdateCustomer(UpdateCustomerRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        var view = await dispatcher.SendAsync(
            new UpdateCustomerCommand(Nullify(request.DisplayName), Nullify(request.Email)),
            context.CancellationToken).ConfigureAwait(false);

        return DirectoryProtoMapper.ToProto(view);
    }

    public override async Task<ProtoCustomer> AddFavoriteAddress(
        AddFavoriteAddressRequest request,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        var view = await dispatcher.SendAsync(
            new AddFavoriteAddressCommand(
                request.Label,
                DirectoryProtoMapper.ToInput(request.Address),
                request.SetAsDefault),
            context.CancellationToken).ConfigureAwait(false);

        return DirectoryProtoMapper.ToProto(view);
    }

    public override async Task<ProtoCustomer> UpdateFavoriteAddress(
        UpdateFavoriteAddressRequest request,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        var view = await dispatcher.SendAsync(
            new UpdateFavoriteAddressCommand(
                ParseId(request.AddressId, "adresse"),
                request.Label,
                DirectoryProtoMapper.ToInput(request.Address),
                request.SetAsDefault),
            context.CancellationToken).ConfigureAwait(false);

        return DirectoryProtoMapper.ToProto(view);
    }

    public override async Task<ProtoCustomer> RemoveFavoriteAddress(
        RemoveFavoriteAddressRequest request,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        var view = await dispatcher.SendAsync(
            new RemoveFavoriteAddressCommand(ParseId(request.AddressId, "adresse")),
            context.CancellationToken).ConfigureAwait(false);

        return DirectoryProtoMapper.ToProto(view);
    }

    // ------------------------------------------------------ Commerçant ---

    public override async Task<ProtoMerchant> GetMerchant(GetMerchantRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        var view = await dispatcher.QueryAsync(
            new GetMerchantQuery(Nullify(request.MerchantId)),
            context.CancellationToken).ConfigureAwait(false);

        return DirectoryProtoMapper.ToProto(view);
    }

    public override async Task<ListMerchantsResponse> ListMerchants(
        ListMerchantsRequest request,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        var page = await dispatcher.QueryAsync(
            new ListMerchantsQuery(Nullify(request.Query), request.OnlyActive, request.PageSize, request.Offset),
            context.CancellationToken).ConfigureAwait(false);

        var response = new ListMerchantsResponse { Total = page.Total };
        response.Merchants.AddRange(page.Merchants.Select(DirectoryProtoMapper.ToProto));

        return response;
    }

    public override async Task<ProtoMerchant> CreateMerchant(CreateMerchantRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        var view = await dispatcher.SendAsync(
            new CreateMerchantCommand(
                request.LegalName,
                request.ContactName,
                request.ContactPhone,
                Nullify(request.ContactEmail),
                request.AveragePreparationMinutes),
            context.CancellationToken).ConfigureAwait(false);

        return DirectoryProtoMapper.ToProto(view);
    }

    public override async Task<ProtoMerchant> UpdateMerchant(UpdateMerchantRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        var view = await dispatcher.SendAsync(
            new UpdateMerchantCommand(
                Nullify(request.MerchantId),
                Nullify(request.LegalName),
                Nullify(request.ContactName),
                Nullify(request.ContactPhone),
                Nullify(request.ContactEmail),
                request.AveragePreparationMinutes == 0 ? null : request.AveragePreparationMinutes),
            context.CancellationToken).ConfigureAwait(false);

        return DirectoryProtoMapper.ToProto(view);
    }

    // ------------------------------------------------ Points de collecte ---

    public override async Task<ProtoMerchant> AddPickupPoint(AddPickupPointRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        var view = await dispatcher.SendAsync(
            new AddPickupPointCommand(
                Nullify(request.MerchantId),
                request.Name,
                DirectoryProtoMapper.ToInput(request.Address),
                DirectoryProtoMapper.ToInput(request.OpeningHours)),
            context.CancellationToken).ConfigureAwait(false);

        return DirectoryProtoMapper.ToProto(view);
    }

    public override async Task<ProtoMerchant> UpdatePickupPoint(
        UpdatePickupPointRequest request,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        var view = await dispatcher.SendAsync(
            new UpdatePickupPointCommand(
                Nullify(request.MerchantId),
                ParseId(request.PickupPointId, "point de collecte"),
                request.Name,
                DirectoryProtoMapper.ToInput(request.Address),
                DirectoryProtoMapper.ToInput(request.OpeningHours)),
            context.CancellationToken).ConfigureAwait(false);

        return DirectoryProtoMapper.ToProto(view);
    }

    public override async Task<ProtoMerchant> SetPickupPointActive(
        SetPickupPointActiveRequest request,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        var view = await dispatcher.SendAsync(
            new SetPickupPointActiveCommand(
                Nullify(request.MerchantId),
                ParseId(request.PickupPointId, "point de collecte"),
                request.Active),
            context.CancellationToken).ConfigureAwait(false);

        return DirectoryProtoMapper.ToProto(view);
    }

    public override async Task<ProtoPickupPoint> GetPickupPoint(
        GetPickupPointRequest request,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        var view = await dispatcher.QueryAsync(
            new GetPickupPointQuery(ParseId(request.PickupPointId, "point de collecte")),
            context.CancellationToken).ConfigureAwait(false);

        return DirectoryProtoMapper.ToProto(view);
    }

    private static Guid ParseId(string value, string label)
        => Guid.TryParse(value, out var id)
            ? id
            : throw new DomainException("INVALID_ID", $"Identifiant de {label} invalide : {value}.");

    private static string? Nullify(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
