using FluentAssertions;
using Hba.BuildingBlocks.Domain;
using Hba.Delivery.Domain.Deliveries;
using Hba.Delivery.Domain.Deliveries.Events;
using Xunit;

namespace Hba.Delivery.Domain.Tests;

public sealed class DeliveryStateMachineTests
{
    [Fact]
    public void Une_livraison_naît_en_attente_de_paiement()
    {
        var delivery = DeliveryBuilder.Created();

        delivery.Status.Should().Be(DeliveryStatus.PendingPayment);
        delivery.DomainEvents.Should().ContainSingle(e => e is DeliveryCreated);
    }

    [Fact]
    public void Aucune_recherche_de_livreur_avant_la_confirmation_du_paiement()
    {
        var delivery = DeliveryBuilder.Created();

        var act = () => delivery.StartDriverSearch(Actor.DispatchEngine, DeliveryBuilder.At(1));

        act.Should().Throw<InvalidStateTransitionException>();
    }

    [Fact]
    public void Seul_le_fournisseur_de_paiement_peut_confirmer_le_paiement()
    {
        var delivery = DeliveryBuilder.Created();

        var act = () => delivery.ConfirmPayment(
            DeliveryBuilder.PaymentIntentId,
            Actor.Admin("admin-1"),
            DeliveryBuilder.At(1));

        act.Should().Throw<InvalidStateTransitionException>();
    }

    [Fact]
    public void Le_webhook_de_paiement_rejoué_ne_produit_pas_deux_événements()
    {
        var delivery = DeliveryBuilder.Paid();
        delivery.ClearDomainEvents();

        delivery.ConfirmPayment(DeliveryBuilder.PaymentIntentId, Actor.FedaPay, DeliveryBuilder.At(5));

        delivery.Status.Should().Be(DeliveryStatus.Paid);
        delivery.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void Un_livreur_non_affecté_ne_peut_pas_agir_sur_la_course()
    {
        var delivery = DeliveryBuilder.Assigned();

        var act = () => delivery.MarkArrivedAtPickup(Actor.Driver("driver-999"), DeliveryBuilder.At(10));

        act.Should().Throw<ForbiddenException>();
    }

    [Fact]
    public void Le_livreur_ne_peut_pas_livrer_sans_OTP_valide()
    {
        var delivery = DeliveryBuilder.PickedUp();

        var act = () => delivery.ConfirmDelivery("000000", null, Actor.Driver(DeliveryBuilder.DriverId), DeliveryBuilder.At(30));

        act.Should().Throw<DomainException>().Which.Code.Should().Be("INVALID_OTP");
        delivery.Status.Should().Be(DeliveryStatus.PickedUp);
    }

    [Fact]
    public void Un_OTP_valide_clôt_la_livraison_et_publie_la_rémunération()
    {
        var delivery = DeliveryBuilder.PickedUp();
        var code = delivery.Otp.Code;
        delivery.ClearDomainEvents();

        delivery.ConfirmDelivery(code, "proof/1.jpg", Actor.Driver(DeliveryBuilder.DriverId), DeliveryBuilder.At(30));

        delivery.Status.Should().Be(DeliveryStatus.Delivered);
        delivery.DomainEvents.OfType<DeliveryCompleted>().Should().ContainSingle()
            .Which.DriverEarning.Amount.Should().Be(1100);
    }

    [Fact]
    public void Le_code_de_remise_se_verrouille_après_cinq_échecs()
    {
        var delivery = DeliveryBuilder.PickedUp();
        var driver = Actor.Driver(DeliveryBuilder.DriverId);

        for (var attempt = 0; attempt < 5; attempt++)
        {
            var act = () => delivery.ConfirmDelivery("000000", null, driver, DeliveryBuilder.At(30));
            act.Should().Throw<DomainException>();
        }

        var locked = () => delivery.ConfirmDelivery(delivery.Otp.Code, null, driver, DeliveryBuilder.At(31));

        locked.Should().Throw<DomainException>().Which.Code.Should().Be("OTP_LOCKED");
    }

    [Fact]
    public void Un_administrateur_ne_peut_pas_livrer_à_la_place_du_livreur()
    {
        var delivery = DeliveryBuilder.PickedUp();

        var act = () => delivery.AdminClose(
            DeliveryStatus.Delivered,
            "le client dit avoir reçu",
            Actor.Admin("admin-1"),
            DeliveryBuilder.At(40));

        act.Should().Throw<ForbiddenException>();
        delivery.Status.Should().Be(DeliveryStatus.PickedUp);
    }

    [Theory]
    [InlineData(DeliveryStatus.Failed)]
    [InlineData(DeliveryStatus.Cancelled)]
    public void Un_administrateur_peut_clore_en_échec_ou_en_annulation(DeliveryStatus target)
    {
        var delivery = DeliveryBuilder.PickedUp();

        delivery.AdminClose(target, "incident constaté", Actor.Admin("admin-1"), DeliveryBuilder.At(40));

        delivery.Status.Should().Be(target);
        delivery.ClosureReason.Should().Be("incident constaté");
    }

    [Fact]
    public void Une_livraison_close_ne_bouge_plus()
    {
        var delivery = DeliveryBuilder.Assigned();
        delivery.Cancel("client injoignable", Actor.Customer(DeliveryBuilder.CustomerId), DeliveryBuilder.At(15));

        var act = () => delivery.MarkPickedUp(null, Actor.Driver(DeliveryBuilder.DriverId), DeliveryBuilder.At(20));

        act.Should().Throw<InvalidStateTransitionException>();
    }

    [Fact]
    public void Le_prix_figé_ne_change_pas_après_la_confirmation()
    {
        var delivery = DeliveryBuilder.PickedUp();

        // Aucune méthode publique ne remplace le snapshot : c'est la garantie.
        var setter = typeof(Deliveries.Delivery)
            .GetProperty(nameof(Deliveries.Delivery.Pricing))!
            .GetSetMethod(nonPublic: true);

        setter.Should().NotBeNull();
        setter!.IsPublic.Should().BeFalse();

        delivery.Pricing.Total.Amount.Should().Be(1500);
    }

    [Fact]
    public void Le_livreur_ne_voit_la_destination_qu_après_affectation()
    {
        var delivery = DeliveryBuilder.Searching();

        delivery.CanRevealDropoffTo(Actor.Driver(DeliveryBuilder.DriverId)).Should().BeFalse();

        delivery.AssignDriver(DeliveryBuilder.Driver(), DeliveryBuilder.OfferId, Actor.DispatchEngine, DeliveryBuilder.At(3));

        delivery.CanRevealDropoffTo(Actor.Driver(DeliveryBuilder.DriverId)).Should().BeTrue();
    }
}
