using FluentAssertions;
using Hba.BuildingBlocks.Domain;
using Hba.Driver.Domain.Drivers;
using Hba.Driver.Domain.Exceptions;
using Xunit;

namespace Hba.Driver.Domain.Tests;

/// <summary>
/// Qui a le droit de transporter quoi.
/// </summary>
///
/// <remarks>
/// DEUX VERROUS DIFFERENTS, ET LES CONFONDRE EST L'ERREUR A EVITER.
/// L'habilitation appartient a la PERSONNE : ops l'accorde apres examen du
/// dossier. La possibilite appartient au MATERIEL : un velo ne prend pas de
/// passager, quel que soit le dossier de celui qui pedale. Le dispatch exige
/// les deux, et ces tests les eprouvent separement avant de les eprouver
/// ensemble.
/// </remarks>
public sealed class CapacitesTests
{
    private static readonly Actor Ops = Actor.Human(ActorKind.Admin, "ops-1");
    private static readonly DateTimeOffset Maintenant =
        new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    private static DriverAggregate Livreur(Vehicle vehicule)
        => DriverAggregate.Register(
            Guid.CreateVersion7(), "Kofi", "+22997000000", vehicule, Ops, Maintenant);

    // ---- La table des vehicules ---------------------------------------------

    [Theory]
    [InlineData(VehicleType.Motorcycle)]
    [InlineData(VehicleType.Car)]
    [InlineData(VehicleType.Tricycle)]
    public void Moto_voiture_et_tricycle_transportent_des_personnes(VehicleType type)
    {
        Vehicle.CapacitesDe(type).Should().Contain(DriverCapability.PassengerTransport);
    }

    [Theory]
    [InlineData(VehicleType.Bicycle)]
    [InlineData(VehicleType.Van)]
    public void Le_velo_et_la_camionnette_ne_transportent_pas_de_personnes(VehicleType type)
    {
        Vehicle.CapacitesDe(type).Should().NotContain(DriverCapability.PassengerTransport);
    }

    [Theory]
    [InlineData(VehicleType.Motorcycle)]
    [InlineData(VehicleType.Car)]
    [InlineData(VehicleType.Van)]
    [InlineData(VehicleType.Bicycle)]
    [InlineData(VehicleType.Tricycle)]
    public void Tous_portent_un_colis_et_un_repas(VehicleType type)
    {
        // LE VELO EST DANS CETTE LISTE, ET C'EST TOUT L'OBJET DE SON OUVERTURE
        // le 30 septembre 2026 : un cycliste livre des documents et des repas
        // en centre-ville mieux qu'une moto.
        var capacites = Vehicle.CapacitesDe(type);

        capacites.Should().Contain(DriverCapability.PackageDelivery);
        capacites.Should().Contain(DriverCapability.FoodDelivery);
    }

    [Fact]
    public void Un_type_inconnu_n_autorise_rien()
    {
        // LE REPLI EST LE VIDE, PAS LE TOUT. Une valeur d'enum ajoutee sans
        // qu'on pense a la table n'ouvre ainsi aucun droit — elle bloque, ce qui
        // se voit, au lieu d'autoriser en silence le transport de personnes.
        Vehicle.CapacitesDe(VehicleType.Unspecified).Should().BeEmpty();
    }

    // ---- Les places ----------------------------------------------------------

    [Fact]
    public void Une_voiture_sans_place_declaree_ne_prend_personne()
    {
        var voiture = Vehicle.Create(VehicleType.Car, "AB-1234-RB", 200_000);

        voiture.Autorise(DriverCapability.PassengerTransport).Should().BeTrue();

        // LE TYPE AUTORISE, LE VEHICULE NE PEUT PAS. C'est la seconde condition,
        // celle qu'on oublie : sans elle le dispatch enverrait un passager vers
        // un vehicule dont personne n'a dit s'il a une banquette.
        voiture.PeutTransporterDesPersonnes.Should().BeFalse();
    }

    [Fact]
    public void Une_voiture_avec_places_prend_des_personnes()
    {
        var voiture = Vehicle.Create(
            VehicleType.Car, "AB-1234-RB", 200_000,
            brand: "Toyota", model: "Corolla", color: "bleue", passengerCapacity: 4);

        voiture.PeutTransporterDesPersonnes.Should().BeTrue();
        voiture.Color.Should().Be("bleue");
    }

    [Fact]
    public void Declarer_des_places_dans_un_velo_est_refuse()
    {
        var action = () => Vehicle.Create(
            VehicleType.Bicycle, null, 5_000, passengerCapacity: 1);

        action.Should().Throw<DomainException>()
            .Which.Code.Should().Be(DriverErrorCodes.VehicleCannotCarryPassengers);
    }

    [Fact]
    public void Un_nombre_de_places_negatif_est_refuse()
    {
        var action = () => Vehicle.Create(
            VehicleType.Car, "AB-1234-RB", 200_000, passengerCapacity: -1);

        action.Should().Throw<DomainException>();
    }

    // ---- L'habilitation du livreur -------------------------------------------

    [Fact]
    public void Le_colis_est_acquis_sans_aucune_declaration()
    {
        // LES LIVREURS DEJA INSCRITS N'ONT DECLARE AUCUNE CAPACITE, et ils font
        // tous du colis. Si ce test tombe, la mise en service des capabilities
        // arrete toute la flotte existante du jour au lendemain.
        var livreur = Livreur(Vehicle.Create(VehicleType.Motorcycle, "AB-1234-RB", 20_000));

        livreur.Capabilities.Should().BeEmpty();
        livreur.Habilite(DriverCapability.PackageDelivery).Should().BeTrue();
    }

    [Fact]
    public void Le_transport_de_personnes_doit_etre_declare()
    {
        var livreur = Livreur(Vehicle.Create(VehicleType.Motorcycle, "AB-1234-RB", 20_000));

        livreur.Habilite(DriverCapability.PassengerTransport).Should().BeFalse();

        livreur.DeclareCapabilities(
            [DriverCapability.PackageDelivery, DriverCapability.PassengerTransport],
            Ops, Maintenant);

        livreur.Habilite(DriverCapability.PassengerTransport).Should().BeTrue();
    }

    [Fact]
    public void Un_cycliste_ne_peut_pas_se_declarer_transporteur_de_personnes()
    {
        var cycliste = Livreur(Vehicle.Create(VehicleType.Bicycle, null, 5_000));

        var action = () => cycliste.DeclareCapabilities(
            [DriverCapability.PassengerTransport], Ops, Maintenant);

        // LE REFUS EST IMMEDIAT ET NOMME LA CAPACITE. Accepter la declaration
        // puis ne jamais lui envoyer de course serait un silence impossible a
        // diagnostiquer depuis un telephone.
        action.Should().Throw<DomainException>()
            .Which.Code.Should().Be(DriverErrorCodes.CapabilityNotAllowedByVehicle);
    }

    [Fact]
    public void Une_declaration_remplace_la_precedente()
    {
        var livreur = Livreur(Vehicle.Create(VehicleType.Car, "AB-1234-RB", 200_000));

        livreur.DeclareCapabilities(
            [DriverCapability.PassengerTransport, DriverCapability.FoodDelivery],
            Ops, Maintenant);

        livreur.DeclareCapabilities([DriverCapability.FoodDelivery], Ops, Maintenant);

        // RETIRER DOIT ETRE AUSSI SIMPLE QU'ACCORDER, sinon une habilitation ne
        // se retire jamais.
        livreur.Habilite(DriverCapability.PassengerTransport).Should().BeFalse();
        livreur.Habilite(DriverCapability.FoodDelivery).Should().BeTrue();
    }

    [Fact]
    public void Changer_de_vehicule_retire_ce_que_le_nouveau_ne_permet_pas()
    {
        var livreur = Livreur(Vehicle.Create(VehicleType.Motorcycle, "AB-1234-RB", 20_000));
        livreur.DeclareCapabilities([DriverCapability.PassengerTransport], Ops, Maintenant);

        livreur.DeclareVehicle(Vehicle.Create(VehicleType.Bicycle, null, 5_000), Ops, Maintenant);

        // L'HABILITATION RESTE DANS LE DOSSIER, MAIS N'EST PLUS EXERCABLE. C'est
        // voulu : le livreur qui reprend sa moto demain n'a pas a redemander
        // l'autorisation a ops. C'est « Habilite » qui tranche au moment du
        // dispatch, pas la liste enregistree.
        livreur.Capabilities.Should().Contain(DriverCapability.PassengerTransport);
        livreur.Habilite(DriverCapability.PassengerTransport).Should().BeFalse();
    }

    [Fact]
    public void Unspecified_est_ignore_dans_une_declaration()
    {
        var livreur = Livreur(Vehicle.Create(VehicleType.Car, "AB-1234-RB", 200_000));

        livreur.DeclareCapabilities(
            [DriverCapability.Unspecified, DriverCapability.FoodDelivery], Ops, Maintenant);

        livreur.Capabilities.Should().BeEquivalentTo(new[] { DriverCapability.FoodDelivery });
    }
}
