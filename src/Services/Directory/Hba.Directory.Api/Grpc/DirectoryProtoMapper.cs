using Google.Protobuf.WellKnownTypes;
using Hba.Contracts.Common.V1;
using Hba.Contracts.Directory.V1;
using Hba.Directory.Application.Customers;
using Hba.Directory.Application.Merchants;
using Hba.Directory.Application.Views;
using ProtoAddress = Hba.Contracts.Directory.V1.Address;
using ProtoCustomer = Hba.Contracts.Directory.V1.Customer;
using ProtoMerchant = Hba.Contracts.Directory.V1.Merchant;
using ProtoOpeningHours = Hba.Contracts.Directory.V1.OpeningHours;
using ProtoPickupPoint = Hba.Contracts.Directory.V1.PickupPoint;

namespace Hba.Directory.Api.Grpc;

internal static class DirectoryProtoMapper
{
    public static ProtoCustomer ToProto(CustomerView view)
    {
        ArgumentNullException.ThrowIfNull(view);

        var customer = new ProtoCustomer
        {
            Id = view.Id.ToString(),
            DisplayName = view.DisplayName,
            Phone = view.Phone,
            Email = view.Email ?? string.Empty,
            CreatedAt = Timestamp.FromDateTimeOffset(view.CreatedAt),
        };

        customer.FavoriteAddresses.AddRange(view.FavoriteAddresses.Select(ToProto));

        return customer;
    }

    public static FavoriteAddress ToProto(FavoriteAddressView view)
        => new()
        {
            Id = view.Id.ToString(),
            Label = view.Label,
            Address = ToProto(view.Address),
            IsDefault = view.IsDefault,
            CreatedAt = Timestamp.FromDateTimeOffset(view.CreatedAt),
        };

    public static ProtoMerchant ToProto(MerchantView view)
    {
        ArgumentNullException.ThrowIfNull(view);

        var merchant = new ProtoMerchant
        {
            Id = view.Id.ToString(),
            LegalName = view.LegalName,
            ContactName = view.ContactName,
            ContactPhone = view.ContactPhone,
            ContactEmail = view.ContactEmail ?? string.Empty,
            AveragePreparationMinutes = view.AveragePreparationMinutes,
            IsActive = view.IsActive,
            CreatedAt = Timestamp.FromDateTimeOffset(view.CreatedAt),
        };

        merchant.PickupPoints.AddRange(view.PickupPoints.Select(ToProto));

        return merchant;
    }

    public static ProtoPickupPoint ToProto(PickupPointView view)
    {
        ArgumentNullException.ThrowIfNull(view);

        var point = new ProtoPickupPoint
        {
            Id = view.Id.ToString(),
            MerchantId = view.MerchantId.ToString(),
            Name = view.Name,
            Address = ToProto(view.Address),
            IsActive = view.IsActive,
        };

        point.OpeningHours.AddRange(view.OpeningHours.Select(h => new ProtoOpeningHours
        {
            DayOfWeek = h.IsoDay,
            OpensAtMinutes = h.OpensAtMinutes,
            ClosesAtMinutes = h.ClosesAtMinutes,
        }));

        return point;
    }

    public static ProtoAddress ToProto(AddressView view)
        => new()
        {
            Location = new Location
            {
                Point = new GeoPoint { Latitude = view.Latitude, Longitude = view.Longitude },
                Landmark = view.Landmark,
                Phone = view.Phone,
                ContactName = view.ContactName,
                Notes = view.Notes ?? string.Empty,
            },
        };

    /// <summary>
    /// Une adresse sans point GPS est refusée ici plutôt que plus bas : le
    /// message d'erreur est plus clair pour l'application appelante.
    /// </summary>
    public static AddressInput ToInput(ProtoAddress? address)
    {
        if (address?.Location?.Point is null)
        {
            throw new BuildingBlocks.Domain.DomainException(
                "MISSING_ADDRESS",
                "Une adresse doit comporter un point GPS, un repère et un téléphone.");
        }

        var location = address.Location;

        return new AddressInput(
            location.Point.Latitude,
            location.Point.Longitude,
            location.Landmark,
            location.Phone,
            location.ContactName,
            string.IsNullOrWhiteSpace(location.Notes) ? null : location.Notes);
    }

    public static IReadOnlyList<OpeningHoursInput> ToInput(IEnumerable<ProtoOpeningHours> hours)
        => [.. hours.Select(h => new OpeningHoursInput(h.DayOfWeek, h.OpensAtMinutes, h.ClosesAtMinutes))];
}
