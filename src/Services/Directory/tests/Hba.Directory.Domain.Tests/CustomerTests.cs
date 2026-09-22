using FluentAssertions;
using Hba.BuildingBlocks.Domain;
using Hba.Directory.Domain.Customers;
using Hba.Directory.Domain.Customers.Events;
using Hba.Directory.Domain.ValueObjects;
using Xunit;

namespace Hba.Directory.Domain.Tests;

public sealed class CustomerTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 21, 10, 0, 0, TimeSpan.Zero);

    private static Customer NewCustomer()
    {
        var id = Guid.CreateVersion7();

        return Customer.CreateFromAccount(id, "Hector", "+22997000001", null, Actor.Customer(id.ToString()), Now);
    }

    private static Address NewAddress(string landmark = "Carré 442, Gbedjromede")
        => Address.Create(GeoPoint.Create(6.3703, 2.3912), landmark, "+22997000001", "Hector");

    [Fact]
    public void Un_profil_naît_du_compte_et_publie_son_événement()
    {
        var customer = NewCustomer();

        customer.Phone.Should().Be("+22997000001");
        customer.DomainEvents.Should().ContainSingle(e => e is CustomerProfileCreated);
    }

    [Fact]
    public void La_première_adresse_devient_l_adresse_principale()
    {
        var customer = NewCustomer();

        var address = customer.AddFavoriteAddress("maison", NewAddress(), setAsDefault: false, Now);

        address.IsDefault.Should().BeTrue("un client qui n'a qu'une adresse n'en a pas zéro par défaut");
    }

    [Fact]
    public void Une_seule_adresse_est_principale_à_la_fois()
    {
        var customer = NewCustomer();
        customer.AddFavoriteAddress("maison", NewAddress(), setAsDefault: false, Now);
        customer.AddFavoriteAddress("bureau", NewAddress("Immeuble bleu"), setAsDefault: true, Now);

        customer.FavoriteAddresses.Count(a => a.IsDefault).Should().Be(1);
        customer.FavoriteAddresses.Single(a => a.IsDefault).Label.Should().Be("bureau");
    }

    [Fact]
    public void Supprimer_l_adresse_principale_en_désigne_une_autre()
    {
        var customer = NewCustomer();
        var home = customer.AddFavoriteAddress("maison", NewAddress(), setAsDefault: true, Now);
        customer.AddFavoriteAddress("bureau", NewAddress("Immeuble bleu"), setAsDefault: false, Now);

        customer.RemoveFavoriteAddress(home.Id);

        customer.FavoriteAddresses.Should().ContainSingle();
        customer.FavoriteAddresses.Single().IsDefault.Should().BeTrue();
    }

    [Fact]
    public void Le_nombre_d_adresses_favorites_est_borné()
    {
        var customer = NewCustomer();

        for (var i = 0; i < Customer.MaxFavoriteAddresses; i++)
        {
            customer.AddFavoriteAddress($"adresse {i}", NewAddress(), setAsDefault: false, Now);
        }

        var act = () => customer.AddFavoriteAddress("une de trop", NewAddress(), setAsDefault: false, Now);

        act.Should().Throw<DomainException>().Which.Code.Should().Be("TOO_MANY_ADDRESSES");
    }

    [Fact]
    public void Une_adresse_sans_repère_est_refusée()
    {
        var act = () => Address.Create(GeoPoint.Create(6.37, 2.39), "   ", "+22997000001", "Hector");

        act.Should().Throw<DomainException>().Which.Code.Should().Be("MISSING_LANDMARK");
    }

    [Fact]
    public void Une_adresse_sans_téléphone_valide_est_refusée()
    {
        var act = () => Address.Create(GeoPoint.Create(6.37, 2.39), "Carré 442", "97000001", "Hector");

        act.Should().Throw<DomainException>().Which.Code.Should().Be("INVALID_PHONE");
    }

    [Fact]
    public void Une_adresse_introuvable_lève_NotFound()
    {
        var customer = NewCustomer();

        var act = () => customer.RemoveFavoriteAddress(Guid.CreateVersion7());

        act.Should().Throw<NotFoundException>();
    }
}
