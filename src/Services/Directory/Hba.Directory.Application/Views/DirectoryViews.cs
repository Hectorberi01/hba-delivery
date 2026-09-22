using Hba.Directory.Domain.Customers;
using Hba.Directory.Domain.Merchants;
using Hba.Directory.Domain.ValueObjects;

namespace Hba.Directory.Application.Views;

public sealed record AddressView(
    double Latitude,
    double Longitude,
    string Landmark,
    string Phone,
    string ContactName,
    string? Notes);

public sealed record FavoriteAddressView(
    Guid Id,
    string Label,
    AddressView Address,
    bool IsDefault,
    DateTimeOffset CreatedAt);

public sealed record CustomerView(
    Guid Id,
    string DisplayName,
    string Phone,
    string? Email,
    IReadOnlyList<FavoriteAddressView> FavoriteAddresses,
    DateTimeOffset CreatedAt);

public sealed record OpeningHoursView(int IsoDay, int OpensAtMinutes, int ClosesAtMinutes);

public sealed record PickupPointView(
    Guid Id,
    Guid MerchantId,
    string Name,
    AddressView Address,
    IReadOnlyList<OpeningHoursView> OpeningHours,
    bool IsActive);

public sealed record MerchantView(
    Guid Id,
    string LegalName,
    string ContactName,
    string ContactPhone,
    string? ContactEmail,
    int AveragePreparationMinutes,
    IReadOnlyList<PickupPointView> PickupPoints,
    bool IsActive,
    DateTimeOffset CreatedAt);

public static class DirectoryViewMapper
{
    public static CustomerView ToView(Customer customer)
    {
        ArgumentNullException.ThrowIfNull(customer);

        return new CustomerView(
            customer.Id,
            customer.DisplayName,
            customer.Phone,
            customer.Email,
            [.. customer.FavoriteAddresses.Select(ToView)],
            customer.CreatedAt);
    }

    public static FavoriteAddressView ToView(FavoriteAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);

        return new FavoriteAddressView(
            address.Id,
            address.Label,
            ToView(address.Address),
            address.IsDefault,
            address.CreatedAt);
    }

    public static MerchantView ToView(Merchant merchant)
    {
        ArgumentNullException.ThrowIfNull(merchant);

        return new MerchantView(
            merchant.Id,
            merchant.LegalName,
            merchant.ContactName,
            merchant.ContactPhone,
            merchant.ContactEmail,
            merchant.AveragePreparationMinutes,
            [.. merchant.PickupPoints.Select(point => ToView(merchant.Id, point))],
            merchant.IsActive,
            merchant.CreatedAt);
    }

    public static PickupPointView ToView(Guid merchantId, PickupPoint point)
    {
        ArgumentNullException.ThrowIfNull(point);

        return new PickupPointView(
            point.Id,
            merchantId,
            point.Name,
            ToView(point.Address),
            [.. point.OpeningHours.Select(h => new OpeningHoursView(h.IsoDay, h.OpensAtMinutes, h.ClosesAtMinutes))],
            point.IsActive);
    }

    public static AddressView ToView(Address address)
    {
        ArgumentNullException.ThrowIfNull(address);

        return new AddressView(
            address.Point.Latitude,
            address.Point.Longitude,
            address.Landmark,
            address.Phone,
            address.ContactName,
            address.Notes);
    }
}
