using Hba.BuildingBlocks.Application.Abstractions;
using Hba.BuildingBlocks.Security;
using Hba.Delivery.Domain.Deliveries;
using Hba.Delivery.Domain.ValueObjects;

namespace Hba.Delivery.Application.Views;

/// <summary>
/// Traduit l'agrégat en projection, en retirant ce que l'appelant n'a pas le
/// droit de voir. C'est le seul endroit du service où un agrégat devient un DTO.
/// </summary>
public static class DeliveryViewMapper
{
    public static DeliveryView ToView(DeliveryAggregate delivery, ICallerContext caller)
    {
        ArgumentNullException.ThrowIfNull(delivery);
        ArgumentNullException.ThrowIfNull(caller);

        var isBackOffice = caller.Roles.Overlaps(HbaRoles.BackOffice);
        var isDriver = caller.IsInRole(HbaRoles.Driver);
        var isCustomer = caller.IsInRole(HbaRoles.Customer);
        var isMerchant = caller.IsInRole(HbaRoles.MerchantOwner) || caller.IsInRole(HbaRoles.MerchantStaff);
        var isPartner = caller.IsInRole(HbaRoles.Partner);

        var isAssignedDriver = isDriver && delivery.Driver?.DriverId == caller.DriverId;

        // Le livreur ne voit l'adresse exacte de destination qu'après acceptation.
        var showDropoff = !isDriver || isAssignedDriver;

        // Le téléphone du livreur est visible pendant la mission, pour le client
        // et le commerçant. Jamais pour un partenaire (système), toujours pour
        // le back-office.
        var showDriverPhone = isBackOffice
                              || ((isCustomer || isMerchant) && IsMissionOngoing(delivery.Status));

        // Le détail tarifaire : tout le monde sauf le livreur, qui n'a que sa part.
        var showFullPricing = !isDriver;

        // Le code de remise n'existe que pour le client donneur d'ordre.
        var showOtp = isCustomer && delivery.CustomerId == caller.SubjectId && !delivery.IsClosed;

        return new DeliveryView
        {
            Id = delivery.Id,
            Reference = delivery.Reference,
            Status = delivery.Status,
            Source = delivery.Source,
            PartnerId = isBackOffice || isPartner ? delivery.PartnerId : null,
            ExternalOrderId = isBackOffice || isPartner || isMerchant ? delivery.ExternalOrderId : null,
            CustomerId = isBackOffice ? delivery.CustomerId : null,
            MerchantId = isBackOffice || isMerchant ? delivery.MerchantId : null,
            PickupPointId = delivery.PickupPointId,
            Pickup = ToLocationView(delivery.Pickup, includePhone: true),
            Dropoff = showDropoff ? ToLocationView(delivery.Dropoff, includePhone: true) : null,
            RecipientName = showDropoff ? delivery.Recipient.Name : null,
            RecipientPhone = showDropoff ? delivery.Recipient.Phone : null,
            Pricing = showFullPricing ? ToPricingView(delivery.Pricing) : null,
            DriverEarningXof = isDriver || isBackOffice ? delivery.Pricing.DriverEarning.Amount : null,
            Driver = delivery.Driver is null || isDriver ? null : ToDriverView(delivery.Driver, showDriverPhone),
            DeliveryOtp = showOtp ? delivery.Otp.Code : null,
            PackageDescription = delivery.PackageDescription,
            PackageWeightGrams = delivery.PackageWeightGrams,
            ClosureReason = delivery.ClosureReason,
            CreatedAt = delivery.CreatedAt,
            PaidAt = delivery.PaidAt,
            AssignedAt = delivery.AssignedAt,
            PickedUpAt = delivery.PickedUpAt,
            CompletedAt = delivery.CompletedAt,
        };
    }

    private static bool IsMissionOngoing(DeliveryStatus status)
        => status is DeliveryStatus.DriverAssigned
            or DeliveryStatus.DriverAtPickup
            or DeliveryStatus.PickedUp;

    private static LocationView ToLocationView(Location location, bool includePhone)
        => new(
            location.Point.Latitude,
            location.Point.Longitude,
            location.Landmark,
            includePhone ? location.Phone : null,
            location.ContactName,
            location.Notes);

    private static PricingView ToPricingView(PricingSnapshot pricing)
        => new(
            pricing.Total.Amount,
            pricing.BaseFare.Amount,
            pricing.DistanceFare.Amount,
            pricing.SurgeFare.Amount,
            pricing.DriverEarning.Amount,
            pricing.DistanceMeters,
            pricing.DurationSeconds,
            pricing.TariffVersion);

    private static AssignedDriverView ToDriverView(AssignedDriver driver, bool includePhone)
        => new(
            driver.DriverId,
            driver.DisplayName,
            includePhone ? driver.Phone : null,
            driver.VehicleType,
            driver.VehiclePlate);
}
