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

    /// <summary>
    /// L'acteur d'un geste que le client fait sur sa propre fiche.
    /// </summary>
    ///
    /// <remarks>
    /// IL N'Y A PAS D'ACTEUR « SYSTEME » DANS CE REFERENTIEL, et c'est
    /// volontaire : poser une photo est un geste de QUELQU'UN, et l'audit doit
    /// pouvoir dire de qui. Les seuls acteurs non humains sont le moteur de
    /// dispatch, le prestataire de paiement et le planificateur.
    /// </remarks>
    private static Actor Lui(Customer customer) => Actor.Customer(customer.Id.ToString());

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

    // ------------------------------------------------ Photo de profil ---
    //
    // CE QUI EST TESTE ICI, C'EST LA VALEUR DE RETOUR, et non la colonne. La
    // colonne se lit ; le retour, lui, est la SEULE occasion de nommer la photo
    // qu'on remplace. S'il se trompe, soit un fichier survit a son
    // remplacement sans que rien ne sache le nommer, soit — bien pire — on
    // efface celui qu'on vient d'enregistrer.

    [Fact]
    public void Poser_une_première_photo_ne_remplace_rien()
    {
        var customer = NewCustomer();

        var remplacee = customer.SetPhoto(Guid.CreateVersion7(), Lui(customer), Now);

        remplacee.Should().BeNull("il n'y avait pas de photo avant celle-ci");
        customer.DomainEvents.Should().Contain(e => e is CustomerPhotoChanged);
    }

    [Fact]
    public void Remplacer_une_photo_rend_celle_qu_elle_remplace()
    {
        var customer = NewCustomer();
        var ancienne = Guid.CreateVersion7();
        customer.SetPhoto(ancienne, Lui(customer), Now);

        var remplacee = customer.SetPhoto(Guid.CreateVersion7(), Lui(customer), Now);

        remplacee.Should().Be(ancienne, "c'est l'appelant qui la fera effacer de Media");
    }

    [Fact]
    public void Reposer_la_même_photo_ne_demande_aucune_suppression()
    {
        var customer = NewCustomer();
        var media = Guid.CreateVersion7();
        customer.SetPhoto(media, Lui(customer), Now);

        var remplacee = customer.SetPhoto(media, Lui(customer), Now);

        remplacee.Should().BeNull(
            "rendre cet identifiant ferait effacer le fichier que la fiche vient d'accepter");
        customer.PhotoMediaId.Should().Be(media);
    }

    [Fact]
    public void Retirer_la_photo_rend_celle_qu_il_faut_effacer()
    {
        var customer = NewCustomer();
        var media = Guid.CreateVersion7();
        customer.SetPhoto(media, Lui(customer), Now);

        var effacee = customer.RemovePhoto(Lui(customer), Now);

        effacee.Should().Be(media);
        customer.PhotoMediaId.Should().BeNull();
    }

    [Fact]
    public void Retirer_une_photo_absente_ne_fait_rien()
    {
        var customer = NewCustomer();

        var effacee = customer.RemovePhoto(Lui(customer), Now);

        effacee.Should().BeNull();
        customer.DomainEvents.Should().NotContain(e => e is CustomerPhotoChanged);
    }

    [Fact]
    public void Une_photo_sans_identifiant_est_refusée()
    {
        var customer = NewCustomer();

        var act = () => customer.SetPhoto(Guid.Empty, Lui(customer), Now);

        act.Should().Throw<DomainException>().Which.Code.Should().Be("PHOTO_MEDIA_REQUIRED");
    }
}
