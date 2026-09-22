using Hba.BuildingBlocks.Domain;
using Hba.Delivery.Domain.Deliveries;
using Hba.Delivery.Domain.ValueObjects;

namespace Hba.Delivery.Domain.Tests;

/// <summary>
/// Construit une livraison dans l'état voulu, en ne passant que par les méthodes
/// de l'agrégat : un test ne doit jamais pouvoir fabriquer un état impossible.
/// </summary>
internal sealed class DeliveryBuilder
{
    public const string CustomerId = "customer-1";
    public const string DriverId = "driver-1";
    public const string OfferId = "offer-1";
    public const string PaymentIntentId = "pi-1";

    private static readonly DateTimeOffset Now = new(2026, 9, 21, 10, 0, 0, TimeSpan.Zero);

    public static Deliveries.Delivery Created() => Deliveries.Delivery.Create(
        Guid.CreateVersion7(),
        "HBA-ABC234",
        DeliverySource.ClientApp,
        "hba-internal",
        externalOrderId: null,
        customerId: CustomerId,
        merchantId: null,
        pickupPointId: null,
        Location.Create(GeoPoint.Create(6.3703, 2.3912), "Carré 442, Gbedjromede", "+22997000001", "Expéditeur"),
        Location.Create(GeoPoint.Create(6.3654, 2.4183), "Immeuble bleu, Fidjrosse", "+22997000002", "Destinataire"),
        Recipient.Create("Destinataire", "+22997000002"),
        Pricing(),
        "Colis",
        1200,
        Actor.Customer(CustomerId),
        Now);

    public static Deliveries.Delivery Paid()
    {
        var delivery = Created();
        delivery.ConfirmPayment(PaymentIntentId, Actor.FedaPay, Now.AddMinutes(1));
        return delivery;
    }

    public static Deliveries.Delivery Searching()
    {
        var delivery = Paid();
        delivery.StartDriverSearch(Actor.DispatchEngine, Now.AddMinutes(2));
        return delivery;
    }

    public static Deliveries.Delivery Assigned()
    {
        var delivery = Searching();
        delivery.AssignDriver(Driver(), OfferId, Actor.DispatchEngine, Now.AddMinutes(3));
        return delivery;
    }

    public static Deliveries.Delivery PickedUp()
    {
        var delivery = Assigned();
        delivery.MarkArrivedAtPickup(Actor.Driver(DriverId), Now.AddMinutes(10));
        delivery.MarkPickedUp(null, Actor.Driver(DriverId), Now.AddMinutes(12));
        return delivery;
    }

    public static AssignedDriver Driver() =>
        AssignedDriver.Create(DriverId, "Koffi A.", "+22997000003", VehicleType.Motorcycle, "AB-1234-RB");

    public static PricingSnapshot Pricing() => PricingSnapshot.Create(
        "quote-1",
        "2026-09",
        MoneyXof.From(1500),
        MoneyXof.From(800),
        MoneyXof.From(700),
        MoneyXof.Zero,
        MoneyXof.From(1100),
        3400,
        720,
        Now);

    public static DateTimeOffset At(int minutes) => Now.AddMinutes(minutes);
}
