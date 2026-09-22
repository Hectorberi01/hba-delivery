using Hba.BuildingBlocks.Application.Abstractions;
using Hba.BuildingBlocks.Application.Messaging;
using Hba.BuildingBlocks.Domain;
using Hba.BuildingBlocks.Security;
using Hba.Directory.Application.Authorization;
using Hba.Directory.Application.Customers;
using Hba.Directory.Application.Ports;
using Hba.Directory.Application.Views;
using Hba.Directory.Domain.Merchants;
using Hba.Directory.Domain.ValueObjects;

namespace Hba.Directory.Application.Merchants;

internal static class OpeningHoursFactory
{
    public static IReadOnlyList<OpeningHours> From(IReadOnlyList<OpeningHoursInput>? input)
        => input is null
            ? []
            : [.. input.Select(h => OpeningHours.Create(h.IsoDay, h.OpensAtMinutes, h.ClosesAtMinutes))];
}

public sealed class GetMerchantHandler(
    IMerchantRepository merchants,
    ICallerContext caller) : IQueryHandler<GetMerchantQuery, MerchantView>
{
    public async Task<MerchantView> HandleAsync(GetMerchantQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var id = DirectoryAccess.ResolveMerchantId(caller, query.MerchantId);

        var merchant = await merchants.GetByIdAsync(id, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException("Commerçant", id.ToString());

        return DirectoryViewMapper.ToView(merchant);
    }
}

public sealed class ListMerchantsHandler(
    IMerchantRepository merchants,
    ICallerContext caller) : IQueryHandler<ListMerchantsQuery, MerchantPage>
{
    private const int MaxPageSize = 100;

    public async Task<MerchantPage> HandleAsync(ListMerchantsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        // Parcourir l'annuaire des commerçants n'a de sens que pour le
        // back-office : un commerçant n'a pas à connaître ses concurrents.
        DirectoryAccess.EnsureBackOffice(caller);

        var pageSize = Math.Clamp(query.PageSize <= 0 ? 25 : query.PageSize, 1, MaxPageSize);
        var offset = Math.Max(0, query.Offset);

        var page = await merchants
            .SearchAsync(query.Query, query.OnlyActive, pageSize, offset, cancellationToken)
            .ConfigureAwait(false);

        var total = await merchants
            .CountAsync(query.Query, query.OnlyActive, cancellationToken)
            .ConfigureAwait(false);

        return new MerchantPage([.. page.Select(DirectoryViewMapper.ToView)], total);
    }
}

public sealed class CreateMerchantHandler(
    IMerchantRepository merchants,
    IUnitOfWork unitOfWork,
    ICallerContext caller,
    IClock clock) : ICommandHandler<CreateMerchantCommand, MerchantView>
{
    public async Task<MerchantView> HandleAsync(CreateMerchantCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        // Un commerçant est référencé par HBA, pas par lui-même : sinon
        // n'importe qui s'inscrit comme commerce et demande des livraisons.
        if (!caller.IsInRole(HbaRoles.Admin) && !caller.IsInRole(HbaRoles.Ops))
        {
            throw new ForbiddenException("Seul le back-office référence un commerçant.");
        }

        var merchant = Merchant.Create(
            command.LegalName,
            command.ContactName,
            command.ContactPhone,
            command.ContactEmail,
            command.AveragePreparationMinutes,
            caller.ToActor(),
            clock.UtcNow);

        merchants.Add(merchant);

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return DirectoryViewMapper.ToView(merchant);
    }
}

/// <summary>
/// Socle des commandes qui modifient un commerçant : résoudre le périmètre,
/// vérifier le droit, charger, appliquer, enregistrer.
/// </summary>
public abstract class MerchantCommandHandlerBase(
    IMerchantRepository merchants,
    IUnitOfWork unitOfWork,
    ICallerContext caller,
    IClock clock)
{
    protected IClock Clock => clock;

    protected ICallerContext Caller => caller;

    protected async Task<MerchantView> ApplyAsync(
        string? requestedMerchantId,
        Action<Merchant> apply,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(apply);

        DirectoryAccess.EnsureCanManageMerchant(caller);

        var id = DirectoryAccess.ResolveMerchantId(caller, requestedMerchantId);

        var merchant = await merchants.GetByIdAsync(id, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException("Commerçant", id.ToString());

        apply(merchant);

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return DirectoryViewMapper.ToView(merchant);
    }
}

public sealed class UpdateMerchantHandler(
    IMerchantRepository merchants,
    IUnitOfWork unitOfWork,
    ICallerContext caller,
    IClock clock)
    : MerchantCommandHandlerBase(merchants, unitOfWork, caller, clock),
      ICommandHandler<UpdateMerchantCommand, MerchantView>
{
    public Task<MerchantView> HandleAsync(UpdateMerchantCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return ApplyAsync(
            command.MerchantId,
            merchant => merchant.Update(
                command.LegalName,
                command.ContactName,
                command.ContactPhone,
                command.ContactEmail,
                command.AveragePreparationMinutes,
                Caller.ToActor(),
                Clock.UtcNow),
            cancellationToken);
    }
}

public sealed class AddPickupPointHandler(
    IMerchantRepository merchants,
    IUnitOfWork unitOfWork,
    ICallerContext caller,
    IClock clock)
    : MerchantCommandHandlerBase(merchants, unitOfWork, caller, clock),
      ICommandHandler<AddPickupPointCommand, MerchantView>
{
    public Task<MerchantView> HandleAsync(AddPickupPointCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return ApplyAsync(
            command.MerchantId,
            merchant => merchant.AddPickupPoint(
                command.Name,
                AddressFactory.From(command.Address),
                OpeningHoursFactory.From(command.OpeningHours),
                Caller.ToActor(),
                Clock.UtcNow),
            cancellationToken);
    }
}

public sealed class UpdatePickupPointHandler(
    IMerchantRepository merchants,
    IUnitOfWork unitOfWork,
    ICallerContext caller,
    IClock clock)
    : MerchantCommandHandlerBase(merchants, unitOfWork, caller, clock),
      ICommandHandler<UpdatePickupPointCommand, MerchantView>
{
    public Task<MerchantView> HandleAsync(UpdatePickupPointCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return ApplyAsync(
            command.MerchantId,
            merchant => merchant.UpdatePickupPoint(
                command.PickupPointId,
                command.Name,
                AddressFactory.From(command.Address),
                OpeningHoursFactory.From(command.OpeningHours),
                Caller.ToActor(),
                Clock.UtcNow),
            cancellationToken);
    }
}

public sealed class SetPickupPointActiveHandler(
    IMerchantRepository merchants,
    IUnitOfWork unitOfWork,
    ICallerContext caller,
    IClock clock)
    : MerchantCommandHandlerBase(merchants, unitOfWork, caller, clock),
      ICommandHandler<SetPickupPointActiveCommand, MerchantView>
{
    public Task<MerchantView> HandleAsync(SetPickupPointActiveCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return ApplyAsync(
            command.MerchantId,
            merchant => merchant.SetPickupPointActive(
                command.PickupPointId,
                command.Active,
                Caller.ToActor(),
                Clock.UtcNow),
            cancellationToken);
    }
}

public sealed class GetPickupPointHandler(
    IMerchantRepository merchants,
    ICallerContext caller) : IQueryHandler<GetPickupPointQuery, PickupPointView>
{
    public async Task<PickupPointView> HandleAsync(GetPickupPointQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (!caller.IsAuthenticated)
        {
            throw new ForbiddenException("Appel non authentifié.");
        }

        var merchant = await merchants
            .GetByPickupPointIdAsync(query.PickupPointId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new NotFoundException("Point de collecte", query.PickupPointId.ToString());

        // Un employé du commerce voisin n'a rien à lire ici ; un client, si,
        // puisqu'il va y faire enlever son colis.
        var isOwnMerchant = string.Equals(caller.MerchantId, merchant.Id.ToString(), StringComparison.Ordinal);
        var isMerchantUser = caller.IsInRole(HbaRoles.MerchantOwner) || caller.IsInRole(HbaRoles.MerchantStaff);

        if (isMerchantUser && !isOwnMerchant)
        {
            throw new NotFoundException("Point de collecte", query.PickupPointId.ToString());
        }

        var point = merchant.FindPickupPoint(query.PickupPointId);

        return DirectoryViewMapper.ToView(merchant.Id, point);
    }
}
