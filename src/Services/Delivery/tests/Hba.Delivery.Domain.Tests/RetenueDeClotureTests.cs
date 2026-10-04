using FluentAssertions;
using Hba.BuildingBlocks.Domain;
using Hba.Delivery.Domain.Deliveries;
using Hba.Delivery.Domain.Deliveries.Events;
using Hba.Delivery.Domain.ValueObjects;
using Xunit;

namespace Hba.Delivery.Domain.Tests;

/// <summary>
/// Ce que la clôture d'une course laisse au livreur.
/// </summary>
///
/// <remarks>
/// LA RÈGLE TIENT EN UNE LIGNE ET SE CASSE EN SILENCE. Sans livreur affecté, la
/// retenue est nulle quel que soit le taux de la zone ; avec un livreur, c'est le
/// montant figé par le devis. Si cette ligne se trompe, personne ne le voit : un
/// client se verrait prélever une retenue pour une course que personne n'a
/// acceptée, ou un livreur ne serait pas dédommagé d'un déplacement réel. Ni l'un
/// ni l'autre ne produit d'erreur, de journal, ni d'écran rouge.
///
/// POURQUOI LES ASSERTIONS PORTENT SUR L'ÉVÉNEMENT ET NON SUR L'AGRÉGAT. C'est
/// l'événement qui voyage jusqu'à Payment, et c'est lui qui décide ce qui est
/// crédité et ce qui est remboursé. L'agrégat garde son instantané tarifaire intact
/// dans tous les cas — le lire ne dirait rien de la règle.
/// </remarks>
public sealed class RetenueDeClotureTests
{
    private const long Retenue = 300;

    [Fact]
    public void Annuler_avant_toute_affectation_ne_retient_rien()
    {
        var course = DeliveryBuilder.Searching(Retenue);
        course.ClearDomainEvents();

        course.Cancel("Le client s'est trompé d'adresse.", Actor.Customer(DeliveryBuilder.CustomerId), DeliveryBuilder.At(5));

        course.DomainEvents.OfType<DeliveryCancelled>().Should().ContainSingle()
            .Which.CancellationFeeXof.Should().Be(0);
    }

    /// <summary>
    /// LE TAUX DE LA ZONE NE CHANGE RIEN TANT QUE PERSONNE NE S'EST DÉPLACÉ, et
    /// c'est le cœur de la règle : la retenue paie un déplacement, pas une
    /// commande.
    /// </summary>
    [Fact]
    public void Annuler_avant_affectation_ne_retient_rien_meme_avec_un_taux_eleve()
    {
        var course = DeliveryBuilder.Paid(retenueXof: 1_400);
        course.ClearDomainEvents();

        course.Cancel("Changement d'avis.", Actor.Customer(DeliveryBuilder.CustomerId), DeliveryBuilder.At(2));

        course.DomainEvents.OfType<DeliveryCancelled>().Should().ContainSingle()
            .Which.CancellationFeeXof.Should().Be(0);
    }

    [Fact]
    public void Annuler_apres_affectation_retient_le_montant_du_devis()
    {
        var course = DeliveryBuilder.Assigned(Retenue);
        course.ClearDomainEvents();

        course.Cancel("Le client n'est plus là.", Actor.Customer(DeliveryBuilder.CustomerId), DeliveryBuilder.At(6));

        course.DomainEvents.OfType<DeliveryCancelled>().Should().ContainSingle()
            .Which.CancellationFeeXof.Should().Be(Retenue);
    }

    /// <summary>
    /// UNE GRILLE SANS RETENUE LAISSE L'ANNULATION GRATUITE, même après affectation.
    /// C'est le défaut de toutes les grilles antérieures au 2 octobre 2026.
    /// </summary>
    [Fact]
    public void Une_grille_sans_retenue_laisse_l_annulation_gratuite()
    {
        var course = DeliveryBuilder.Assigned(retenueXof: 0);
        course.ClearDomainEvents();

        course.Cancel("Changement d'avis.", Actor.Customer(DeliveryBuilder.CustomerId), DeliveryBuilder.At(6));

        course.DomainEvents.OfType<DeliveryCancelled>().Should().ContainSingle()
            .Which.CancellationFeeXof.Should().Be(0);
    }

    /// <summary>
    /// LE POINT 34 : un incident déclaré par le livreur suit la même règle qu'une
    /// annulation. Il s'est déplacé et a porté le colis.
    /// </summary>
    [Fact]
    public void Un_incident_declare_par_le_livreur_dedommage_comme_une_annulation()
    {
        var course = DeliveryBuilder.PickedUp(Retenue);
        course.ClearDomainEvents();

        course.DeclareIncident(
            "Destinataire injoignable.",
            Actor.Driver(DeliveryBuilder.DriverId),
            DeliveryBuilder.At(40));

        course.DomainEvents.OfType<DeliveryFailed>().Should().ContainSingle()
            .Which.CancellationFeeXof.Should().Be(Retenue);
    }

    /// <summary>
    /// UNE CLÔTURE ADMINISTRATIVE PASSE PAR LA MÊME RÈGLE, et c'est voulu : le
    /// livreur s'est déplacé que la course soit close par le client ou par
    /// l'exploitation.
    /// </summary>
    [Fact]
    public void Une_cloture_administrative_apres_affectation_retient_aussi()
    {
        var course = DeliveryBuilder.Assigned(Retenue);
        course.ClearDomainEvents();

        course.AdminClose(
            DeliveryStatus.Cancelled,
            "Course close par l'exploitation.",
            Actor.Admin("admin-1"),
            DeliveryBuilder.At(20));

        course.DomainEvents.OfType<DeliveryCancelled>().Should().ContainSingle()
            .Which.CancellationFeeXof.Should().Be(Retenue);
    }

    /// <summary>
    /// LA FRONTIÈRE VÉRIFIE CE QU'ELLE REÇOIT. Pricing garantit déjà qu'une retenue
    /// ne dépasse pas le total ; cette classe appartient à un AUTRE service et ne
    /// s'en remet pas à cette garantie.
    /// </summary>
    [Fact]
    public void Une_retenue_superieure_au_total_est_refusee()
    {
        var trop = () => PricingSnapshot.Create(
            "quote-1",
            "2026-10",
            MoneyXof.From(1_500),
            MoneyXof.From(800),
            MoneyXof.From(700),
            MoneyXof.Zero,
            MoneyXof.From(1_100),
            3_400,
            720,
            DeliveryBuilder.At(0),
            MoneyXof.From(1_600));

        trop.Should().Throw<DomainException>().Which.Code.Should().Be("INVALID_PRICE");
    }

    [Fact]
    public void Une_retenue_absente_vaut_zero_et_non_une_erreur()
    {
        var sansRetenue = PricingSnapshot.Create(
            "quote-1",
            "2026-10",
            MoneyXof.From(1_500),
            MoneyXof.From(800),
            MoneyXof.From(700),
            MoneyXof.Zero,
            MoneyXof.From(1_100),
            3_400,
            720,
            DeliveryBuilder.At(0));

        sansRetenue.CancellationFee.Amount.Should().Be(0);
    }
}
