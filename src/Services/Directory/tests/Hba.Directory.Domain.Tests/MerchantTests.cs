using FluentAssertions;
using Hba.BuildingBlocks.Domain;
using Hba.Directory.Domain.Merchants;
using Hba.Directory.Domain.Merchants.Events;
using Hba.Directory.Domain.ValueObjects;
using Xunit;

namespace Hba.Directory.Domain.Tests;

public sealed class MerchantTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 21, 10, 0, 0, TimeSpan.Zero);
    private static readonly Actor Ops = Actor.Admin("ops-1");

    private static Merchant NewMerchant() => Merchant.Create(
        "Chez Adjovi SARL",
        "Adjovi",
        "+22997000010",
        "contact@chez-adjovi.bj",
        20,
        Ops,
        Now);

    private static Address NewAddress() =>
        Address.Create(GeoPoint.Create(6.3703, 2.3912), "Carré 442, Gbedjromede", "+22997000010", "Adjovi");

    [Fact]
    public void Un_commerçant_référencé_publie_son_événement()
    {
        var merchant = NewMerchant();

        merchant.IsActive.Should().BeTrue();
        merchant.DomainEvents.Should().ContainSingle(e => e is MerchantCreated);
    }

    [Fact]
    public void Un_temps_de_préparation_aberrant_est_refusé()
    {
        var act = () => Merchant.Create("Boutique", "Contact", "+22997000010", null, 500, Ops, Now);

        act.Should().Throw<DomainException>().Which.Code.Should().Be("INVALID_PREPARATION_TIME");
    }

    [Fact]
    public void Ajouter_un_point_de_collecte_prévient_les_autres_services()
    {
        var merchant = NewMerchant();
        merchant.ClearDomainEvents();

        merchant.AddPickupPoint("Boutique centre", NewAddress(), [], Ops, Now);

        merchant.PickupPoints.Should().ContainSingle();
        merchant.DomainEvents.Should().ContainSingle(e => e is PickupPointChanged);
    }

    [Fact]
    public void Le_dernier_point_de_collecte_actif_ne_peut_pas_être_fermé()
    {
        var merchant = NewMerchant();
        var point = merchant.AddPickupPoint("Boutique centre", NewAddress(), [], Ops, Now);

        var act = () => merchant.SetPickupPointActive(point.Id, active: false, Ops, Now);

        act.Should().Throw<DomainException>().Which.Code.Should().Be("LAST_PICKUP_POINT");
    }

    [Fact]
    public void Désactiver_le_commerçant_ferme_tous_ses_points()
    {
        var merchant = NewMerchant();
        merchant.AddPickupPoint("Boutique centre", NewAddress(), [], Ops, Now);
        merchant.AddPickupPoint("Entrepôt", NewAddress(), [], Ops, Now);

        merchant.Deactivate("cessation d'activité", Ops, Now);

        merchant.IsActive.Should().BeFalse();
        merchant.PickupPoints.Should().OnlyContain(p => !p.IsActive);
        merchant.DomainEvents.Should().Contain(e => e is MerchantDeactivated);
    }

    [Fact]
    public void Un_commerçant_désactivé_n_accepte_plus_de_point_de_collecte()
    {
        var merchant = NewMerchant();
        merchant.AddPickupPoint("Boutique centre", NewAddress(), [], Ops, Now);
        merchant.Deactivate("cessation d'activité", Ops, Now);

        var act = () => merchant.AddPickupPoint("Nouveau", NewAddress(), [], Ops, Now);

        act.Should().Throw<ForbiddenException>();
    }

    [Fact]
    public void Deux_plages_du_même_jour_ne_peuvent_pas_se_chevaucher()
    {
        var merchant = NewMerchant();

        var act = () => merchant.AddPickupPoint(
            "Boutique centre",
            NewAddress(),
            [OpeningHours.Create(1, 480, 720), OpeningHours.Create(1, 700, 1200)],
            Ops,
            Now);

        act.Should().Throw<DomainException>().Which.Code.Should().Be("OVERLAPPING_HOURS");
    }

    [Fact]
    public void Sans_horaire_déclaré_un_point_est_considéré_ouvert()
    {
        var merchant = NewMerchant();
        var point = merchant.AddPickupPoint("Boutique centre", NewAddress(), [], Ops, Now);

        point.IsOpenAt(DayOfWeek.Sunday, 23 * 60).Should().BeTrue();
    }

    [Fact]
    public void Les_horaires_déclarés_sont_respectés()
    {
        var merchant = NewMerchant();

        var point = merchant.AddPickupPoint(
            "Boutique centre",
            NewAddress(),
            [OpeningHours.Create(1, 480, 1200)],
            Ops,
            Now);

        point.IsOpenAt(DayOfWeek.Monday, 600).Should().BeTrue();
        point.IsOpenAt(DayOfWeek.Monday, 1300).Should().BeFalse();
        point.IsOpenAt(DayOfWeek.Tuesday, 600).Should().BeFalse();
    }

    [Fact]
    public void Une_fermeture_après_l_ouverture_est_exigée()
    {
        var act = () => OpeningHours.Create(1, 1200, 480);

        act.Should().Throw<DomainException>().Which.Code.Should().Be("INVALID_HOURS");
    }
}
