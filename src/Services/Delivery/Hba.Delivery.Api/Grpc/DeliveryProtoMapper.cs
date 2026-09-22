using Google.Protobuf.WellKnownTypes;
using Hba.Contracts.Common.V1;
using Hba.Contracts.Delivery.V1;
using Hba.Delivery.Application.Views;
using DomainStatus = Hba.Delivery.Domain.Deliveries.DeliveryStatus;
using DomainVehicle = Hba.Delivery.Domain.Deliveries.VehicleType;
using ProtoDelivery = Hba.Contracts.Delivery.V1.Delivery;

namespace Hba.Delivery.Api.Grpc;

/// <summary>
/// Traduit la projection en message gRPC. La projection a DÉJÀ retiré ce que
/// l'appelant n'a pas le droit de voir : ce mapper ne prend aucune décision de
/// visibilité, il recopie ce qui lui est donné.
/// </summary>
internal static class DeliveryProtoMapper
{
    public static ProtoDelivery ToProto(DeliveryView view)
    {
        ArgumentNullException.ThrowIfNull(view);

        var proto = new ProtoDelivery
        {
            Id = view.Id.ToString(),
            Reference = view.Reference,
            Status = ToProtoStatus(view.Status),
            Source = ToProtoSource(view.Source),
            PartnerId = view.PartnerId ?? string.Empty,
            ExternalOrderId = view.ExternalOrderId ?? string.Empty,
            CustomerId = view.CustomerId ?? string.Empty,
            MerchantId = view.MerchantId ?? string.Empty,
            PickupPointId = view.PickupPointId ?? string.Empty,
            Pickup = ToProtoLocation(view.Pickup),
            RecipientName = view.RecipientName ?? string.Empty,
            RecipientPhone = view.RecipientPhone ?? string.Empty,
            PackageDescription = view.PackageDescription ?? string.Empty,
            PackageWeightGrams = view.PackageWeightGrams,
            ClosureReason = view.ClosureReason ?? string.Empty,
            DeliveryOtp = view.DeliveryOtp ?? string.Empty,
            CreatedAt = Timestamp.FromDateTimeOffset(view.CreatedAt),
        };

        if (view.Dropoff is not null)
        {
            proto.Dropoff = ToProtoLocation(view.Dropoff);
        }

        if (view.Pricing is not null)
        {
            proto.Pricing = ToProtoPricing(view.Pricing);
        }
        else if (view.DriverEarningXof is not null)
        {
            // Vue livreur : seule sa rémunération est renseignée.
            proto.Pricing = new PricingSnapshot { DriverEarning = ToMoney(view.DriverEarningXof.Value) };
        }

        if (view.Driver is not null)
        {
            proto.Driver = new AssignedDriver
            {
                DriverId = view.Driver.DriverId,
                DisplayName = view.Driver.DisplayName,
                Phone = view.Driver.Phone ?? string.Empty,
                VehicleType = ToProtoVehicle(view.Driver.VehicleType),
                VehiclePlate = view.Driver.VehiclePlate,
            };
        }

        if (view.PaidAt is not null)
        {
            proto.PaidAt = Timestamp.FromDateTimeOffset(view.PaidAt.Value);
        }

        if (view.AssignedAt is not null)
        {
            proto.AssignedAt = Timestamp.FromDateTimeOffset(view.AssignedAt.Value);
        }

        if (view.PickedUpAt is not null)
        {
            proto.PickedUpAt = Timestamp.FromDateTimeOffset(view.PickedUpAt.Value);
        }

        if (view.CompletedAt is not null)
        {
            proto.CompletedAt = Timestamp.FromDateTimeOffset(view.CompletedAt.Value);
        }

        return proto;
    }

    public static DomainStatus? FromProtoStatus(DeliveryStatus status) => status switch
    {
        DeliveryStatus.PendingPayment => DomainStatus.PendingPayment,
        DeliveryStatus.PaymentFailed => DomainStatus.PaymentFailed,
        DeliveryStatus.Paid => DomainStatus.Paid,
        DeliveryStatus.SearchingDriver => DomainStatus.SearchingDriver,
        DeliveryStatus.NoDriverFound => DomainStatus.NoDriverFound,
        DeliveryStatus.DriverAssigned => DomainStatus.DriverAssigned,
        DeliveryStatus.DriverAtPickup => DomainStatus.DriverAtPickup,
        DeliveryStatus.PickedUp => DomainStatus.PickedUp,
        DeliveryStatus.Delivered => DomainStatus.Delivered,
        DeliveryStatus.Cancelled => DomainStatus.Cancelled,
        DeliveryStatus.Failed => DomainStatus.Failed,
        _ => null,
    };

    public static Domain.Deliveries.DeliverySource FromProtoSource(Source source) => source switch
    {
        Source.HbaExpress => Domain.Deliveries.DeliverySource.HbaExpress,
        Source.HbaFood => Domain.Deliveries.DeliverySource.HbaFood,
        Source.PartnerApi => Domain.Deliveries.DeliverySource.PartnerApi,
        _ => Domain.Deliveries.DeliverySource.ClientApp,
    };

    private static DeliveryStatus ToProtoStatus(DomainStatus status) => status switch
    {
        DomainStatus.PendingPayment => DeliveryStatus.PendingPayment,
        DomainStatus.PaymentFailed => DeliveryStatus.PaymentFailed,
        DomainStatus.Paid => DeliveryStatus.Paid,
        DomainStatus.SearchingDriver => DeliveryStatus.SearchingDriver,
        DomainStatus.NoDriverFound => DeliveryStatus.NoDriverFound,
        DomainStatus.DriverAssigned => DeliveryStatus.DriverAssigned,
        DomainStatus.DriverAtPickup => DeliveryStatus.DriverAtPickup,
        DomainStatus.PickedUp => DeliveryStatus.PickedUp,
        DomainStatus.Delivered => DeliveryStatus.Delivered,
        DomainStatus.Cancelled => DeliveryStatus.Cancelled,
        DomainStatus.Failed => DeliveryStatus.Failed,
        _ => DeliveryStatus.Unspecified,
    };

    private static Source ToProtoSource(Domain.Deliveries.DeliverySource source) => source switch
    {
        Domain.Deliveries.DeliverySource.HbaExpress => Source.HbaExpress,
        Domain.Deliveries.DeliverySource.HbaFood => Source.HbaFood,
        Domain.Deliveries.DeliverySource.PartnerApi => Source.PartnerApi,
        _ => Source.ClientApp,
    };

    private static Contracts.Common.V1.VehicleType ToProtoVehicle(DomainVehicle type) => type switch
    {
        DomainVehicle.Car => Contracts.Common.V1.VehicleType.Car,
        DomainVehicle.Van => Contracts.Common.V1.VehicleType.Van,
        _ => Contracts.Common.V1.VehicleType.Motorcycle,
    };

    private static Location ToProtoLocation(LocationView view) => new()
    {
        Point = new GeoPoint { Latitude = view.Latitude, Longitude = view.Longitude },
        Landmark = view.Landmark,
        Phone = view.Phone ?? string.Empty,
        ContactName = view.ContactName,
        Notes = view.Notes ?? string.Empty,
    };

    private static PricingSnapshot ToProtoPricing(PricingView view) => new()
    {
        TariffVersion = view.TariffVersion,
        Total = ToMoney(view.TotalXof),
        BaseFare = ToMoney(view.BaseFareXof),
        DistanceFare = ToMoney(view.DistanceFareXof),
        SurgeFare = ToMoney(view.SurgeFareXof),
        DriverEarning = ToMoney(view.DriverEarningXof),
        DistanceMeters = view.DistanceMeters,
        DurationSeconds = view.DurationSeconds,
    };

    private static Money ToMoney(long amount) => new() { Amount = amount, Currency = "XOF" };
}
