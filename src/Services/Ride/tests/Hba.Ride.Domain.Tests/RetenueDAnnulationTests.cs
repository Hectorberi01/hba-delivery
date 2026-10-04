using FluentAssertions;
using Hba.BuildingBlocks.Domain;
using Hba.Ride.Domain.Exceptions;
using Hba.Ride.Domain.Results;
using Hba.Ride.Domain.Rides;
using Hba.Ride.Domain.Rides.Events;
using Xunit;

namespace Hba.Ride.Domain.Tests;

/// <summary>
/// Ce que l'annulation d'une course laisse au chauffeur.
/// </summary>
///
/// <remarks>
/// CE SONT LES PREMIERS TESTS DE RIDE. Le service a été écrit le 1er octobre 2026
/// et portait depuis le prix FERME de toutes les courses de personnes, sans un
/// seul filet.
///
/// ILS COMMENCENT PAR LA RETENUE PARCE QUE C'EST LA RÈGLE QUI VIENT D'ÊTRE ÉCRITE
/// (points 3 et 34, tranchés le 2 octobre 2026). Elle se casse en silence : si
/// elle se trompe, un passager se voit prélever une retenue pour une course que
/// personne n'a acceptée, ou un chauffeur n'est pas dédommagé d'un déplacement
/// réel. Ni l'un ni l'autre ne produit d'erreur, de journal, ni d'écran rouge.
///
/// CE QU'ILS NE COUVRENT PAS, et qu'il faut savoir : la machine à états complète,
/// le prix ferme, l'affectation, le déroulé de la course. Tout cela reste sans
/// test.
/// </remarks>
public sealed class RetenueDAnnulationTests
{
    private static readonly DateTimeOffset Maintenant = new(2026, 10, 2, 14, 0, 0, TimeSpan.Zero);

    private const string Passager = "cus_19af";
    private const string Chauffeur = "drv_7f3a";
    private const long Prix = 2_000;
    private const long Retenue = 400;

    [Fact]
    public void Annuler_avant_toute_affectation_ne_retient_rien()
    {
        var course = Course(Retenue);

        course.Cancel("Je me suis trompé d'adresse.", Actor.Customer(Passager), Maintenant.AddMinutes(1))
            .Succeeded.Should().BeTrue();

        Annulation(course).CancellationFeeXof.Should().Be(0);
    }

    /// <summary>
    /// LE TAUX DE LA ZONE NE CHANGE RIEN TANT QUE PERSONNE NE S'EST DÉPLACÉ. La
    /// retenue paie un déplacement, pas une commande.
    /// </summary>
    [Fact]
    public void Un_taux_eleve_ne_retient_rien_sans_chauffeur()
    {
        var course = Course(retenue: 1_900);

        course.Cancel("Changement d'avis.", Actor.Customer(Passager), Maintenant.AddMinutes(1));

        Annulation(course).CancellationFeeXof.Should().Be(0);
    }

    [Fact]
    public void Annuler_apres_affectation_retient_le_montant_du_devis()
    {
        var course = CourseAvecChauffeur(Retenue);

        course.Cancel("Je n'attends plus.", Actor.Customer(Passager), Maintenant.AddMinutes(6))
            .Succeeded.Should().BeTrue();

        Annulation(course).CancellationFeeXof.Should().Be(Retenue);
    }

    [Fact]
    public void Une_grille_sans_retenue_laisse_l_annulation_gratuite()
    {
        var course = CourseAvecChauffeur(retenue: 0);

        course.Cancel("Changement d'avis.", Actor.Customer(Passager), Maintenant.AddMinutes(6));

        Annulation(course).CancellationFeeXof.Should().Be(0);
    }

    /// <summary>
    /// L'IDENTIFIANT DU CHAUFFEUR FAIT FOI, PAS SON PROFIL. Le profil peut manquer
    /// si Driver était injoignable à l'affectation ; le chauffeur s'est pourtant
    /// bien déplacé, et un dédommagement ne dépend pas de ce qu'on sait de son nom.
    /// </summary>
    [Fact]
    public void Un_profil_manquant_n_empeche_pas_le_dedommagement()
    {
        var course = Course(Retenue);
        course.StartSearching(Actor.Service("dispatch"), Maintenant.AddMinutes(2));
        course.AssignDriver(Chauffeur, profil: null, Actor.Service("dispatch"), Maintenant.AddMinutes(3))
            .Succeeded.Should().BeTrue();

        course.Cancel("Trop long.", Actor.Customer(Passager), Maintenant.AddMinutes(5));

        Annulation(course).CancellationFeeXof.Should().Be(Retenue);
    }

    /// <summary>
    /// LA FRONTIÈRE VÉRIFIE CE QU'ELLE REÇOIT. Pricing garantit déjà qu'une retenue
    /// ne dépasse pas le prix ; Ride est un AUTRE service et ne s'en remet pas à
    /// cette garantie.
    /// </summary>
    [Fact]
    public void Une_retenue_superieure_au_prix_est_refusee()
    {
        var creation = Creer(retenue: Prix + 10);

        creation.Failed.Should().BeTrue();
        creation.Error.Code.Should().Be(RideErrorCodes.ValidationFailed);
    }

    [Fact]
    public void Une_retenue_egale_au_prix_est_acceptee()
    {
        Creer(retenue: Prix).Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Une_course_sans_retenue_se_commande_normalement()
    {
        var creation = Creer(retenue: 0);

        creation.Succeeded.Should().BeTrue();
        creation.Value.CancellationFee.Amount.Should().Be(0);
    }

    private static RideCancelled Annulation(RideAggregate course) =>
        course.DomainEvents.OfType<RideCancelled>().Should().ContainSingle().Subject;

    private static RideAggregate CourseAvecChauffeur(long retenue)
    {
        var course = Course(retenue);
        course.StartSearching(Actor.Service("dispatch"), Maintenant.AddMinutes(2));
        course.AssignDriver(
            Chauffeur,
            AssignedDriver.Create(Chauffeur, "Koffi A.", "+22997000003", VehicleType.Car, "AB-1234-RB"),
            Actor.Service("dispatch"),
            Maintenant.AddMinutes(3));
        return course;
    }

    private static RideAggregate Course(long retenue) => Creer(retenue).Value;

    private static Result<RideAggregate> Creer(long retenue) => RideAggregate.Request(
        Guid.CreateVersion7(),
        Passager,
        pickupLatitude: 6.3703,
        pickupLongitude: 2.3912,
        destinationLatitude: 6.3654,
        destinationLongitude: 2.4183,
        VehicleType.Car,
        estimatedDistanceMeters: 3_400,
        estimatedDurationSeconds: 720,
        estimatedPrice: Prix,
        driverEarning: 1_600,
        cancellationFee: retenue,
        Actor.Customer(Passager),
        Maintenant);
}
