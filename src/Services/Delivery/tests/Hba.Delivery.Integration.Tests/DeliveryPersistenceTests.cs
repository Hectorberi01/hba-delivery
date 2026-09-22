using FluentAssertions;
using Hba.BuildingBlocks.Domain;
using Hba.Delivery.Domain.Deliveries;
using Hba.Delivery.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Hba.Delivery.Integration.Tests;

[Collection(PostgresCollection.Name)]
public sealed class DeliveryPersistenceTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Une_livraison_se_relit_à_l_identique()
    {
        var delivery = NewDelivery();

        await using (var write = fixture.CreateContext())
        {
            write.Deliveries.Add(delivery);
            await write.SaveChangesAsync(CancellationToken.None);
        }

        await using var read = fixture.CreateContext();
        var reloaded = await read.Deliveries.FirstAsync(d => d.Id == delivery.Id, CancellationToken.None);

        reloaded.Reference.Should().Be(delivery.Reference);
        reloaded.Status.Should().Be(DeliveryStatus.PendingPayment);
        reloaded.Pickup.Landmark.Should().Be(delivery.Pickup.Landmark);
        reloaded.Recipient.Phone.Should().Be(delivery.Recipient.Phone);
        reloaded.Pricing.Total.Amount.Should().Be(1500);
        reloaded.Otp.Code.Should().Be(delivery.Otp.Code);
    }

    [Fact]
    public async Task Un_partenaire_ne_peut_pas_créer_deux_fois_la_même_commande_externe()
    {
        var first = NewDelivery(partnerId: "partner-x", externalOrderId: "CMD-1");
        var second = NewDelivery(partnerId: "partner-x", externalOrderId: "CMD-1");

        await using var context = fixture.CreateContext();
        context.Deliveries.Add(first);
        await context.SaveChangesAsync(CancellationToken.None);

        context.Deliveries.Add(second);

        var act = async () => await context.SaveChangesAsync(CancellationToken.None);

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    private static Domain.Deliveries.Delivery NewDelivery(
        string partnerId = "hba-internal",
        string? externalOrderId = null)
        => Domain.Deliveries.Delivery.Create(
            Guid.CreateVersion7(),
            $"HBA-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}",
            DeliverySource.ClientApp,
            partnerId,
            externalOrderId,
            "customer-1",
            null,
            null,
            Location.Create(GeoPoint.Create(6.3703, 2.3912), "Carré 442", "+22997000001", "Expéditeur"),
            Location.Create(GeoPoint.Create(6.3654, 2.4183), "Immeuble bleu", "+22997000002", "Destinataire"),
            Recipient.Create("Destinataire", "+22997000002"),
            PricingSnapshot.Create(
                "quote-1",
                "2026-09",
                MoneyXof.From(1500),
                MoneyXof.From(800),
                MoneyXof.From(700),
                MoneyXof.Zero,
                MoneyXof.From(1100),
                3400,
                720,
                DateTimeOffset.UtcNow),
            "Colis",
            1200,
            Actor.Customer("customer-1"),
            DateTimeOffset.UtcNow);
}
