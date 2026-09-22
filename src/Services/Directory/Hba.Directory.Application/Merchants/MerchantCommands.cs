using Hba.BuildingBlocks.Application.Messaging;
using Hba.Directory.Application.Customers;
using Hba.Directory.Application.Views;

namespace Hba.Directory.Application.Merchants;

public sealed record OpeningHoursInput(int IsoDay, int OpensAtMinutes, int ClosesAtMinutes);

public sealed record GetMerchantQuery(string? MerchantId) : IQuery<MerchantView>;

public sealed record ListMerchantsQuery(string? Query, bool OnlyActive, int PageSize, int Offset)
    : IQuery<MerchantPage>;

public sealed record MerchantPage(IReadOnlyList<MerchantView> Merchants, int Total);

public sealed record CreateMerchantCommand(
    string LegalName,
    string ContactName,
    string ContactPhone,
    string? ContactEmail,
    int AveragePreparationMinutes) : ICommand<MerchantView>;

public sealed record UpdateMerchantCommand(
    string? MerchantId,
    string? LegalName,
    string? ContactName,
    string? ContactPhone,
    string? ContactEmail,
    int? AveragePreparationMinutes) : ICommand<MerchantView>;

public sealed record AddPickupPointCommand(
    string? MerchantId,
    string Name,
    AddressInput Address,
    IReadOnlyList<OpeningHoursInput> OpeningHours) : ICommand<MerchantView>;

public sealed record UpdatePickupPointCommand(
    string? MerchantId,
    Guid PickupPointId,
    string Name,
    AddressInput Address,
    IReadOnlyList<OpeningHoursInput> OpeningHours) : ICommand<MerchantView>;

public sealed record SetPickupPointActiveCommand(string? MerchantId, Guid PickupPointId, bool Active)
    : ICommand<MerchantView>;

/// <summary>
/// Lecture ciblée d'un point de collecte, pour pré-remplir une demande de
/// livraison sans charger tout le commerçant.
/// </summary>
public sealed record GetPickupPointQuery(Guid PickupPointId) : IQuery<PickupPointView>;
