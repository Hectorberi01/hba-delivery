using FluentAssertions;
using Hba.BuildingBlocks.Domain;
using Hba.Pricing.Domain.Quotes;
using Hba.Pricing.Domain.Tariffs;
using Hba.Pricing.Domain.ValueObjects;
using Xunit;

namespace Hba.Pricing.Domain.Tests;

/// <summary>
/// Le devis ne paie que le trajet qu'il a chiffré.
/// </summary>
///
/// <remarks>
/// CES TESTS SONT NES D'UNE FUITE D'ARGENT, le 30 septembre 2026. Le devis
/// portait ses deux points depuis toujours, et personne ne les comparait au
/// trajet de la livraison qui le consommait. Un devis de 300 m payait une course
/// de 20 km : l'ADR 0004 figeait ensuite le prix avec application, le paiement
/// était encaissé au tarif court, et la part du livreur calculée dessus.
///
/// Ce sont aussi les premiers tests de Pricing. Le service calculait le prix de
/// toutes les courses sans un seul filet.
/// </remarks>
public sealed class QuoteConsumptionTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 30, 10, 0, 0, TimeSpan.Zero);

    /// <summary>Ganhi, Cotonou.</summary>
    private static readonly GeoPoint Collecte = GeoPoint.Create(6.3703, 2.3912);

    /// <summary>Fidjrosse, à quelques kilomètres.</summary>
    private static readonly GeoPoint Remise = GeoPoint.Create(6.3654, 2.4183);

    /// <summary>Calavi : une vingtaine de kilomètres plus au nord.</summary>
    private static readonly GeoPoint Calavi = GeoPoint.Create(6.4500, 2.3500);

    [Fact]
    public void Le_trajet_chiffre_se_consomme_normalement()
    {
        var devis = Devis();
        var livraison = Guid.CreateVersion7();

        devis.Consume(livraison, Collecte, Remise, Now.AddMinutes(2));

        devis.IsConsumed.Should().BeTrue();
        devis.ConsumedByDeliveryId.Should().Be(livraison);
    }

    [Fact]
    public void Un_devis_court_ne_paie_pas_une_course_longue()
    {
        var devis = Devis();

        // L'ATTAQUE, EN UNE LIGNE : le même devis, une remise vingt kilomètres
        // plus loin. C'est ce que faisait passer la version du 29 septembre.
        var act = () => devis.Consume(Guid.CreateVersion7(), Collecte, Calavi, Now.AddMinutes(2));

        act.Should().Throw<DomainException>().Which.Code.Should().Be("QUOTE_TRIP_MISMATCH");
        devis.IsConsumed.Should().BeFalse("un devis refusé reste utilisable pour le bon trajet");
    }

    [Fact]
    public void Un_point_de_collecte_deplace_est_refuse_aussi()
    {
        var devis = Devis();

        var act = () => devis.Consume(Guid.CreateVersion7(), Calavi, Remise, Now.AddMinutes(2));

        act.Should().Throw<DomainException>().Which.Code.Should().Be("QUOTE_TRIP_MISMATCH");
    }

    [Fact]
    public void Un_ecart_de_cent_metres_reste_accepte()
    {
        var devis = Devis();

        // Environ 111 m : l'ordre de grandeur d'un aller-retour JSON et d'un
        // arrondi de flottant, pas celui d'un changement d'adresse.
        var collecte = GeoPoint.Create(Collecte.Latitude + 0.001, Collecte.Longitude);
        var remise = GeoPoint.Create(Remise.Latitude, Remise.Longitude + 0.001);

        devis.Consume(Guid.CreateVersion7(), collecte, remise, Now.AddMinutes(2));

        devis.IsConsumed.Should().BeTrue();
    }

    [Fact]
    public void Un_ecart_d_un_kilometre_est_refuse()
    {
        var devis = Devis();

        // Environ 1,1 km : au-delà de la tolérance, et déjà assez pour changer
        // le prix. La limite ne se négocie pas par réglage.
        var remise = GeoPoint.Create(Remise.Latitude + 0.01, Remise.Longitude);

        var act = () => devis.Consume(Guid.CreateVersion7(), Collecte, remise, Now.AddMinutes(2));

        act.Should().Throw<DomainException>().Which.Code.Should().Be("QUOTE_TRIP_MISMATCH");
    }

    [Fact]
    public void Le_message_de_refus_ne_dit_pas_de_combien_l_ecart_depasse()
    {
        var devis = Devis();

        var act = () => devis.Consume(Guid.CreateVersion7(), Collecte, Calavi, Now.AddMinutes(2));

        // CHIFFRER L'ECART APPRENDRAIT OU EST LA LIMITE, donc comment se tenir
        // juste en dessous. Le message dit quoi faire, pas combien il manque.
        var message = act.Should().Throw<DomainException>().Which.Message;
        message.Should().NotContain("250");
        message.Should().Contain("nouveau");
    }

    [Fact]
    public void Un_devis_expire_est_refuse_avant_meme_de_regarder_le_trajet()
    {
        var devis = Devis();

        // L'ORDRE COMPTE POUR LE MESSAGE. Un client dont le devis a simplement
        // expiré doit lire « expiré », pas « autre trajet » : les deux ne
        // demandent pas la même chose de sa part.
        var act = () => devis.Consume(Guid.CreateVersion7(), Collecte, Calavi, Now.AddMinutes(20));

        act.Should().Throw<DomainException>().Which.Code.Should().Be("QUOTE_EXPIRED");
    }

    [Fact]
    public void Un_second_preneur_est_refuse_meme_avec_le_bon_trajet()
    {
        var devis = Devis();
        devis.Consume(Guid.CreateVersion7(), Collecte, Remise, Now.AddMinutes(2));

        var act = () => devis.Consume(Guid.CreateVersion7(), Collecte, Remise, Now.AddMinutes(3));

        act.Should().Throw<DomainException>().Which.Code.Should().Be("QUOTE_ALREADY_CONSUMED");
    }

    [Fact]
    public void La_meme_livraison_peut_redemander_sans_etre_recontrolee()
    {
        var devis = Devis();
        var livraison = Guid.CreateVersion7();
        devis.Consume(livraison, Collecte, Remise, Now.AddMinutes(2));

        // LE REJEU SORT AVANT LE CONTROLE DE TRAJET, ET C'EST SANS DANGER : le
        // devis est déjà lié à CETTE livraison, dont le prix est figé depuis le
        // premier appel. Rien ne bouge ici, quels que soient les points passés.
        // Ce test existe pour que l'ordre reste un choix, pas un oubli.
        var act = () => devis.Consume(livraison, Collecte, Calavi, Now.AddMinutes(3));

        act.Should().NotThrow();
        devis.ConsumedByDeliveryId.Should().Be(livraison);
    }

    [Fact]
    public void La_distance_entre_deux_points_est_celle_du_terrain()
    {
        // Un degré de latitude vaut environ 111 km ; un centième, 1,11 km. La
        // formule n'a pas besoin d'être exacte au mètre, elle doit être juste
        // d'ordre de grandeur, sinon la tolérance ne veut rien dire.
        var depart = GeoPoint.Create(6.3703, 2.3912);
        var arrivee = GeoPoint.Create(6.3803, 2.3912);

        depart.DistanceEnMetresVers(arrivee).Should().BeApproximately(1112, 15);
        depart.DistanceEnMetresVers(depart).Should().Be(0);
    }

    private static Quote Devis() => Quote.Create(
        Guid.CreateVersion7(),
        Grille(),
        Collecte,
        Remise,
        RouteMeasurement.Create(3400, 720),
        Now,
        TimeSpan.FromMinutes(15));

    private static Tariff Grille() => Tariff.Create(
        Guid.CreateVersion7(),
        "2026-09",
        "cotonou-centre",
        VehicleType.Motorcycle,
        baseFare: MoneyXof.From(300),
        perKilometer: MoneyXof.From(150),
        perMinute: MoneyXof.From(10),
        minimumFare: MoneyXof.From(500),
        surgeBasisPoints: 10_000,
        driverShareBasisPoints: 7_500,
        Now.AddDays(-30));
}
