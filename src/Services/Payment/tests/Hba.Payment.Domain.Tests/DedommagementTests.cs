using FluentAssertions;
using Hba.BuildingBlocks.Domain;
using Hba.Payment.Domain.Earnings;
using Hba.Payment.Domain.Payments;
using Xunit;

namespace Hba.Payment.Domain.Tests;

/// <summary>
/// Ce que touche un livreur quand la mission n'aboutit pas.
/// </summary>
///
/// <remarks>
/// CE SONT LES PREMIERS TESTS DE PAYMENT, et le service tenait déjà tout
/// l'argent de la plateforme : les intentions de paiement, le grand livre des
/// livreurs, les demandes de versement. Six mille lignes d'infrastructure, mille
/// deux cents de domaine, aucun filet.
///
/// ILS COMMENCENT PAR LE DÉDOMMAGEMENT PARCE QUE C'EST LA RÈGLE QUI VIENT
/// D'ÊTRE ÉCRITE (points 3 et 34, tranchés le 2 octobre 2026), et qu'une règle
/// d'argent posée sans test est une règle qu'on croit sur parole. Ce n'est pas
/// la couverture la plus large de Payment ; c'est celle dont l'absence se
/// paierait le plus vite.
///
/// CE QU'ILS NE COUVRENT PAS, et qu'il faut savoir : PaymentIntent et sa machine
/// à états, le rejeu d'un webhook, PayoutRequest, RefundDue. Tout cela reste
/// sans test.
/// </remarks>
public sealed class DedommagementTests
{
    private static readonly DateTimeOffset Maintenant = new(2026, 10, 2, 11, 30, 0, TimeSpan.Zero);

    private const string Livreur = "drv_7f3a";

    [Fact]
    public void Un_dedommagement_crediteur_porte_la_nature_et_la_mission()
    {
        var mission = Guid.CreateVersion7();

        var ligne = DriverLedgerEntry.ForCompensation(
            Guid.CreateVersion7(),
            Livreur,
            amountXof: 480,
            ServiceType.Ride,
            mission,
            referenceLabel: "CRS-00412",
            Maintenant);

        ligne.Direction.Should().Be(LedgerDirection.Credit);
        ligne.AmountXof.Should().Be(480);
        ligne.ServiceType.Should().Be(ServiceType.Ride);
        ligne.ReferenceId.Should().Be(mission);
        ligne.ReferenceLabel.Should().Be("CRS-00412");
        ligne.OccurredAt.Should().Be(Maintenant);
    }

    /// <summary>
    /// LE GENRE EST CE QUI PERMETTRA DE RÉPONDRE, DANS QUELQUES SEMAINES, À LA
    /// SEULE QUESTION QUI COMPTE : combien coûte ce qui n'aboutit pas.
    /// </summary>
    ///
    /// <remarks>
    /// Si un dédommagement s'écrivait en DeliveryEarning, il se fondrait dans les
    /// gains ordinaires et la question deviendrait impossible à poser sans relire
    /// chaque mission dans un autre service.
    /// </remarks>
    [Fact]
    public void Un_dedommagement_ne_se_confond_pas_avec_un_gain_de_mission()
    {
        var dedommagement = Dedommagement(300);

        var gain = DriverLedgerEntry.ForMission(
            Guid.CreateVersion7(),
            Livreur,
            amountXof: 300,
            ServiceType.Ride,
            Guid.CreateVersion7(),
            referenceLabel: "CRS-00413",
            Maintenant);

        dedommagement.Kind.Should().Be(LedgerEntryKind.Compensation);
        gain.Kind.Should().Be(LedgerEntryKind.DeliveryEarning);
        dedommagement.Kind.Should().NotBe(gain.Kind);
    }

    /// <summary>
    /// SANS RETENUE, PAS DE LIGNE — et c'est le refus qui l'impose.
    /// </summary>
    ///
    /// <remarks>
    /// Une grille sans taux rend l'annulation gratuite : il n'y a alors rien à
    /// dédommager. Une ligne à zéro au relevé d'un livreur se lirait comme une
    /// erreur de calcul, et la somme de son compte n'y gagnerait rien.
    /// </remarks>
    [Fact]
    public void Un_dedommagement_nul_est_refuse()
    {
        var zero = () => Dedommagement(0);

        zero.Should().Throw<DomainException>()
            .Which.Code.Should().Be("NEGATIVE_LEDGER_AMOUNT");
    }

    [Fact]
    public void Un_dedommagement_negatif_est_refuse()
    {
        var negatif = () => Dedommagement(-100);

        negatif.Should().Throw<DomainException>()
            .Which.Code.Should().Be("NEGATIVE_LEDGER_AMOUNT");
    }

    /// <summary>
    /// LA NATURE EST EXIGÉE POUR LA MÊME RAISON QUE SUR UN GAIN : deux missions au
    /// même identifiant, l'une en colis l'autre en course, partageraient sinon la
    /// ligne qui empêche de créditer deux fois.
    /// </summary>
    [Fact]
    public void Un_dedommagement_sans_nature_est_refuse()
    {
        var sansNature = () => DriverLedgerEntry.ForCompensation(
            Guid.CreateVersion7(),
            Livreur,
            amountXof: 480,
            ServiceType.Unspecified,
            Guid.CreateVersion7(),
            referenceLabel: null,
            Maintenant);

        sansNature.Should().Throw<DomainException>()
            .Which.Code.Should().Be("MISSING_REFERENCE");
    }

    [Fact]
    public void Un_dedommagement_sans_livreur_est_refuse()
    {
        var sansLivreur = () => DriverLedgerEntry.ForCompensation(
            Guid.CreateVersion7(),
            "   ",
            amountXof: 480,
            ServiceType.Delivery,
            Guid.CreateVersion7(),
            referenceLabel: null,
            Maintenant);

        sansLivreur.Should().Throw<ArgumentException>();
    }

    /// <summary>
    /// UNE COURSE DE PERSONNES N'A PAS ENCORE DE RÉFÉRENCE LISIBLE : Ride n'en
    /// attribue pas. Le champ reste alors vide plutôt que nul, et le relevé
    /// retombe sur l'identifiant.
    /// </summary>
    [Fact]
    public void Une_reference_absente_laisse_un_libelle_vide_et_non_nul()
    {
        var ligne = DriverLedgerEntry.ForCompensation(
            Guid.CreateVersion7(),
            Livreur,
            amountXof: 480,
            ServiceType.Ride,
            Guid.CreateVersion7(),
            referenceLabel: null,
            Maintenant);

        ligne.ReferenceLabel.Should().BeEmpty();
    }

    /// <summary>
    /// L'INSTANT EST CELUI DU FAIT, PAS DE L'ÉCRITURE. Un rattrapage d'événements
    /// écrit aujourd'hui des lignes datées d'hier, et c'est hier qui doit figurer
    /// au relevé.
    /// </summary>
    [Fact]
    public void La_date_est_celle_du_fait_pas_celle_de_l_ecriture()
    {
        var hier = Maintenant.AddDays(-1);

        var ligne = DriverLedgerEntry.ForCompensation(
            Guid.CreateVersion7(),
            Livreur,
            amountXof: 480,
            ServiceType.Delivery,
            Guid.CreateVersion7(),
            referenceLabel: "HBA-00891",
            hier);

        ligne.OccurredAt.Should().Be(hier);
    }

    /// <summary>
    /// AUCUN VERSEMENT N'EST RATTACHÉ À UN DÉDOMMAGEMENT : c'est un crédit, pas une
    /// sortie d'argent. L'identifiant de versement reste nul, et c'est lui qui porte
    /// l'index unique interdisant de payer deux fois.
    /// </summary>
    [Fact]
    public void Un_dedommagement_ne_porte_aucun_versement()
    {
        Dedommagement(480).PayoutId.Should().BeNull();
    }

    private static DriverLedgerEntry Dedommagement(long montant) =>
        DriverLedgerEntry.ForCompensation(
            Guid.CreateVersion7(),
            Livreur,
            montant,
            ServiceType.Delivery,
            Guid.CreateVersion7(),
            referenceLabel: "HBA-00742",
            Maintenant);
}
