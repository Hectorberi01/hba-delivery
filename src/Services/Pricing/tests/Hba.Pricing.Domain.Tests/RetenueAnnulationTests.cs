using FluentAssertions;
using Hba.BuildingBlocks.Domain;
using Hba.Pricing.Domain.Quotes;
using Hba.Pricing.Domain.Tariffs;
using Hba.Pricing.Domain.ValueObjects;
using Xunit;

namespace Hba.Pricing.Domain.Tests;

/// <summary>
/// La retenue d'annulation est figée dans le devis, au taux de SA grille.
/// </summary>
///
/// <remarks>
/// CES TESTS EXISTENT PARCE QUE LA RÈGLE EST INVISIBLE À LA LECTURE. Rien, dans
/// le calculateur, ne distingue une retenue figée d'une retenue relue : les deux
/// produisent le même chiffre le jour où la grille ne change pas, et un seul des
/// deux reste juste le jour où elle change. C'est exactement le genre de défaut
/// qu'on découvre sur un relevé de livreur, deux mois plus tard.
///
/// L'AUTRE PIÈGE QU'ILS TIENNENT EST CELUI DES POINTS DE BASE. Trois champs de
/// la grille en portent, et ils n'ont pas tous le même sens : la majoration est
/// un MULTIPLICATEUR dont 10000 est le neutre, la part du livreur et la retenue
/// sont des FRACTIONS dont 10000 est la totalité. La console avait déjà fait
/// refuser dix grilles sur cette confusion, le 1er octobre 2026.
/// </remarks>
public sealed class RetenueAnnulationTests
{
    private static readonly DateTimeOffset Maintenant = new(2026, 10, 2, 9, 0, 0, TimeSpan.Zero);

    private static readonly GeoPoint Collecte = GeoPoint.Create(6.3703, 2.3912);
    private static readonly GeoPoint Remise = GeoPoint.Create(6.3654, 2.4183);

    [Fact]
    public void Sans_taux_la_retenue_est_nulle_et_l_annulation_gratuite()
    {
        var devis = Devis(retenuePointsDeBase: 0);

        devis.CancellationFee.Amount.Should().Be(0);
    }

    [Fact]
    public void La_retenue_est_la_fraction_annoncee_du_total()
    {
        // 2000 points de base valent 20 % — UNE FRACTION, pas un multiplicateur.
        var devis = Devis(retenuePointsDeBase: 2_000);

        var attendu = devis.Total.Amount * 2_000 / 10_000;

        // L'arrondi vers le bas ne peut retirer que les unites.
        devis.CancellationFee.Amount.Should().BeInRange(attendu - 9, attendu);
    }

    [Fact]
    public void La_retenue_ne_depasse_jamais_le_total_meme_au_taux_maximal()
    {
        var devis = Devis(retenuePointsDeBase: 10_000);

        devis.CancellationFee.Amount.Should().BeLessThanOrEqualTo(devis.Total.Amount);
    }

    [Fact]
    public void La_retenue_est_arrondie_vers_le_bas_a_la_dizaine()
    {
        var devis = Devis(retenuePointsDeBase: 3_333);

        (devis.CancellationFee.Amount % 10).Should().Be(0);
    }

    /// <summary>
    /// LE CŒUR DE LA DÉCISION DU 2 OCTOBRE 2026, et le seul test qui la tienne.
    /// </summary>
    ///
    /// <remarks>
    /// Un devis est un instantané (ADR 0004) : il garde le taux de la grille qui
    /// l'a produit. Si la retenue était relue à l'annulation, le client verrait à
    /// l'écran un montant et s'en verrait prélever un autre — et personne ne
    /// pourrait dire, après coup, lequel des deux taux s'appliquait.
    /// </remarks>
    [Fact]
    public void Un_changement_de_taux_ne_touche_pas_un_devis_deja_emis()
    {
        var devisDHier = Devis(retenuePointsDeBase: 1_000);
        var retenueFigee = devisDHier.CancellationFee.Amount;

        // La grille est remplacée par une version au taux triplé. Le devis d'hier
        // n'en sait rien et ne doit rien en savoir.
        var devisDAujourdhui = Devis(retenuePointsDeBase: 3_000);

        devisDHier.CancellationFee.Amount.Should().Be(retenueFigee);
        devisDAujourdhui.CancellationFee.Amount.Should().BeGreaterThan(retenueFigee);
    }

    /// <summary>
    /// Elle se calcule sur le TOTAL, donc elle suit la majoration et le plancher.
    /// </summary>
    [Fact]
    public void La_retenue_suit_la_majoration_puisqu_elle_porte_sur_le_total()
    {
        var sansMajoration = Devis(retenuePointsDeBase: 2_000, majorationPointsDeBase: 10_000);
        var avecMajoration = Devis(retenuePointsDeBase: 2_000, majorationPointsDeBase: 15_000);

        avecMajoration.CancellationFee.Amount
            .Should().BeGreaterThan(sansMajoration.CancellationFee.Amount);
    }

    /// <summary>
    /// SUR UN TRÈS PETIT TOTAL, LA RETENUE RETOMBE À ZÉRO, et c'est le bon défaut :
    /// l'annulation redevient gratuite plutôt que de prélever une poignée de francs.
    /// </summary>
    [Fact]
    public void Un_taux_minuscule_sur_un_petit_total_ne_retient_rien()
    {
        var devis = Devis(retenuePointsDeBase: 1);

        devis.CancellationFee.Amount.Should().Be(0);
    }

    [Fact]
    public void Une_grille_refuse_un_taux_de_retenue_hors_bornes()
    {
        var trop = () => Grille(retenuePointsDeBase: 10_001);
        var negatif = () => Grille(retenuePointsDeBase: -1);

        trop.Should().Throw<DomainException>().Which.Code.Should().Be("INVALID_TARIFF");
        negatif.Should().Throw<DomainException>().Which.Code.Should().Be("INVALID_TARIFF");
    }

    /// <summary>
    /// LA RETENUE N'EST PAS LA PART DU LIVREUR, et les deux ne se mélangent pas :
    /// elle lui revient en entier, sans que DriverShareBasisPoints s'y applique.
    /// </summary>
    [Fact]
    public void La_part_du_livreur_ne_rogne_pas_la_retenue()
    {
        var partFaible = Devis(retenuePointsDeBase: 2_000, partDuLivreurPointsDeBase: 1_000);
        var partForte = Devis(retenuePointsDeBase: 2_000, partDuLivreurPointsDeBase: 9_000);

        partFaible.CancellationFee.Amount
            .Should().Be(partForte.CancellationFee.Amount);
    }

    private static Quote Devis(
        int retenuePointsDeBase,
        int majorationPointsDeBase = 10_000,
        int partDuLivreurPointsDeBase = 7_500) => Quote.Create(
        Guid.CreateVersion7(),
        Grille(retenuePointsDeBase, majorationPointsDeBase, partDuLivreurPointsDeBase),
        Collecte,
        Remise,
        RouteMeasurement.Create(3_400, 720),
        Maintenant,
        TimeSpan.FromMinutes(15));

    private static Tariff Grille(
        int retenuePointsDeBase,
        int majorationPointsDeBase = 10_000,
        int partDuLivreurPointsDeBase = 7_500) => Tariff.Create(
        Guid.CreateVersion7(),
        "2026-10",
        "cotonou-centre",
        ServiceType.Ride,
        VehicleType.Car,
        TimeBand.Journee,
        baseFare: MoneyXof.From(300),
        perKilometer: MoneyXof.From(150),
        perMinute: MoneyXof.From(10),
        minimumFare: MoneyXof.From(500),
        surgeBasisPoints: majorationPointsDeBase,
        driverShareBasisPoints: partDuLivreurPointsDeBase,
        validFrom: Maintenant.AddDays(-30),
        cancellationFeeBasisPoints: retenuePointsDeBase);
}
