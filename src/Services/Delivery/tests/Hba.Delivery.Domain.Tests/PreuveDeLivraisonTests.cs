using FluentAssertions;
using Hba.BuildingBlocks.Domain;
using Hba.Delivery.Domain.Deliveries;
using Hba.Delivery.Domain.Deliveries.Events;
using Xunit;
using DeliveryAggregate = Hba.Delivery.Domain.Deliveries.Delivery;

namespace Hba.Delivery.Domain.Tests;

/// <summary>
/// Ce que l'agrégat accepte comme preuve, et ce qu'il refuse.
/// </summary>
///
/// <remarks>
/// CES REGLES NAISSENT DU POINT 7, tranché le 30 septembre 2026 : la photo est
/// PROPOSEE aux deux étapes, l'étape ne l'attend jamais, et « une photo
/// rattrapée plus tard ne prouverait plus le même instant ». Le dépôt passe par
/// Delivery — « Delivery porte les octets » —, donc c'est ici, et pas chez Media,
/// que se vérifie le droit de joindre une pièce.
///
/// CE QUI EST EPROUVE N'EST PAS UNE LISTE DE CAS, C'EST UNE FRONTIERE : une
/// preuve se rattache à une étape QUI A EU LIEU, par le livreur QUI L'A FAITE,
/// TANT QUE c'est encore le même moment, et UNE SEULE FOIS.
/// </remarks>
public sealed class PreuveDeLivraisonTests
{
    private static readonly Guid Photo = Guid.CreateVersion7();
    private static readonly Guid AutrePhoto = Guid.CreateVersion7();

    private static Actor Livreur => Actor.Driver(DeliveryBuilder.DriverId);

    [Fact]
    public void Une_photo_de_collecte_se_rattache_a_la_collecte()
    {
        var course = DeliveryBuilder.PickedUp();
        course.ClearDomainEvents();

        course.AttacherLaPreuve(EtapeDeLaPreuve.Collecte, Photo, Livreur, DeliveryBuilder.At(13));

        course.PickupProofMediaId.Should().Be(Photo);
        course.DeliveryProofMediaId.Should().BeNull(because: "l'autre étape n'a rien reçu");

        course.DomainEvents.OfType<PreuveAttachee>().Should().ContainSingle()
            .Which.Should().BeEquivalentTo(
                new { Etape = EtapeDeLaPreuve.Collecte, MediaId = Photo },
                options => options.ExcludingMissingMembers());
    }

    [Fact]
    public void Une_photo_de_remise_se_rattache_a_la_remise()
    {
        var course = DeliveryBuilder.Delivered();

        course.AttacherLaPreuve(EtapeDeLaPreuve.Remise, Photo, Livreur, DeliveryBuilder.At(31));

        course.DeliveryProofMediaId.Should().Be(Photo);
    }

    [Fact]
    public void L_etape_doit_avoir_eu_lieu()
    {
        // UNE PREUVE DE CE QUI N'EST PAS ARRIVE N'EST PAS UNE PREUVE. La course
        // est affectée, le livreur n'a pas encore le colis.
        var course = DeliveryBuilder.Assigned();

        var acte = () => course.AttacherLaPreuve(
            EtapeDeLaPreuve.Collecte, Photo, Livreur, DeliveryBuilder.At(5));

        acte.Should().Throw<DomainException>().Which.Code.Should().Be("PROOF_STEP_NOT_DONE");
    }

    [Fact]
    public void Une_course_collectee_n_accepte_pas_encore_de_preuve_de_remise()
    {
        var course = DeliveryBuilder.PickedUp();

        var acte = () => course.AttacherLaPreuve(
            EtapeDeLaPreuve.Remise, Photo, Livreur, DeliveryBuilder.At(13));

        acte.Should().Throw<DomainException>().Which.Code.Should().Be("PROOF_STEP_NOT_DONE");
    }

    [Fact]
    public void Une_course_annulee_n_accepte_pas_de_preuve_de_remise()
    {
        // LE CAS QUI A FAIT CHOISIR LE STATUT PLUTOT QUE « CompletedAt » : une
        // annulation pose elle aussi une date de clôture. Sans cette condition,
        // une course annulée aurait accepté la preuve d'une remise qui n'a
        // jamais eu lieu.
        var course = DeliveryBuilder.PickedUp();
        course.DeclareIncident("destinataire introuvable", Livreur, DeliveryBuilder.At(25));

        course.Status.Should().NotBe(DeliveryStatus.Delivered);

        var acte = () => course.AttacherLaPreuve(
            EtapeDeLaPreuve.Remise, Photo, Livreur, DeliveryBuilder.At(26));

        acte.Should().Throw<DomainException>().Which.Code.Should().Be("PROOF_STEP_NOT_DONE");
    }

    [Fact]
    public void Un_autre_livreur_ne_joint_rien()
    {
        var course = DeliveryBuilder.PickedUp();

        var acte = () => course.AttacherLaPreuve(
            EtapeDeLaPreuve.Collecte, Photo, Actor.Driver("driver-999"), DeliveryBuilder.At(13));

        acte.Should().Throw<ForbiddenException>();
    }

    [Fact]
    public void Le_client_ne_joint_rien_non_plus()
    {
        // LA PREUVE ENGAGE LE LIVREUR : elle ne peut venir que de lui. Un client
        // qui pourrait la déposer fabriquerait la pièce censée l'opposer.
        var course = DeliveryBuilder.PickedUp();

        var acte = () => course.AttacherLaPreuve(
            EtapeDeLaPreuve.Collecte, Photo, Actor.Customer(DeliveryBuilder.CustomerId), DeliveryBuilder.At(13));

        acte.Should().Throw<ForbiddenException>();
    }

    [Fact]
    public void Une_preuve_ne_se_remplace_pas()
    {
        var course = DeliveryBuilder.PickedUp();
        course.AttacherLaPreuve(EtapeDeLaPreuve.Collecte, Photo, Livreur, DeliveryBuilder.At(13));

        var acte = () => course.AttacherLaPreuve(
            EtapeDeLaPreuve.Collecte, AutrePhoto, Livreur, DeliveryBuilder.At(14));

        acte.Should().Throw<DomainException>().Which.Code.Should().Be("PROOF_ALREADY_ATTACHED");
        course.PickupProofMediaId.Should().Be(Photo, because: "la première reste");
    }

    [Fact]
    public void Le_meme_identifiant_deux_fois_est_un_rejeu_et_non_un_echec()
    {
        // LE TELEPHONE QUI N'A PAS RECU LA REPONSE RENVOIE. Il ne doit pas lire
        // un échec là où tout s'est bien passé — et l'agrégat ne doit pas lever
        // un second événement pour un seul dépôt.
        var course = DeliveryBuilder.PickedUp();
        course.AttacherLaPreuve(EtapeDeLaPreuve.Collecte, Photo, Livreur, DeliveryBuilder.At(13));
        course.ClearDomainEvents();

        var acte = () => course.AttacherLaPreuve(
            EtapeDeLaPreuve.Collecte, Photo, Livreur, DeliveryBuilder.At(14));

        acte.Should().NotThrow();
        course.PickupProofMediaId.Should().Be(Photo);
        course.DomainEvents.OfType<PreuveAttachee>().Should().BeEmpty();
    }

    [Fact]
    public void Une_etape_trop_ancienne_n_accepte_plus_rien()
    {
        // « UNE PHOTO RATTRAPEE PLUS TARD NE PROUVERAIT PLUS LE MEME INSTANT »
        // — point 7. La collecte a eu lieu à la minute 12.
        //
        // LE TEST NE CITE PAS LA DUREE, IL LIT LA CONSTANTE, et ce n'est pas de
        // la paresse : un test qui écrirait « 30 » en dur passerait encore le
        // jour où quelqu'un change le nombre sans regarder ce qu'il borne. Ici,
        // c'est la FRONTIERE qui est éprouvée, pas sa valeur.
        var course = DeliveryBuilder.PickedUp();

        var acte = () => course.AttacherLaPreuve(
            EtapeDeLaPreuve.Collecte, Photo, Livreur, DeliveryBuilder.At(12) + DeliveryAggregate.FenetreDeDepotDeLaPreuve + TimeSpan.FromSeconds(1));

        acte.Should().Throw<DomainException>().Which.Code.Should().Be("PROOF_TOO_LATE");
    }

    [Fact]
    public void Juste_avant_la_fin_de_la_fenetre_passe_encore()
    {
        var course = DeliveryBuilder.PickedUp();

        var acte = () => course.AttacherLaPreuve(
            EtapeDeLaPreuve.Collecte, Photo, Livreur, DeliveryBuilder.At(12) + DeliveryAggregate.FenetreDeDepotDeLaPreuve);

        acte.Should().NotThrow(because: "la borne est inclusive, et un test qui l'ignore la ferait glisser");
    }

    [Fact]
    public void La_fenetre_vaut_trente_minutes_parce_que_cela_a_ete_tranche()
    {
        // LE SEUL TEST DE CE FICHIER QUI CITE LE NOMBRE, et c'est delibere : les
        // autres eprouvent la FRONTIERE et lisent la constante, celui-ci garde
        // la DECISION. Tranchee le 30 septembre 2026 au point 7.
        //
        // CE QUE CES TRENTE MINUTES ACHETENT : six minutes d'envoi dans le pire
        // cas — trois de delai, doublees par le rejeu qui suit un jeton expire —
        // et le reste absorbe une horloge de telephone qui retarde. La
        // soustraction mele en effet DEUX horloges : l'etape est datee par le
        // telephone, le depot par le serveur.
        //
        // LE JOUR OU QUELQU'UN CHANGE CE NOMBRE, ce test tombe et le fait
        // relire. C'est tout ce qu'on lui demande.
        DeliveryAggregate.FenetreDeDepotDeLaPreuve.Should().Be(TimeSpan.FromMinutes(30));
    }

    [Fact]
    public void Un_media_vide_ne_designe_rien()
    {
        var course = DeliveryBuilder.PickedUp();

        var acte = () => course.AttacherLaPreuve(
            EtapeDeLaPreuve.Collecte, Guid.Empty, Livreur, DeliveryBuilder.At(13));

        acte.Should().Throw<DomainException>().Which.Code.Should().Be("PROOF_MEDIA_REQUIRED");
    }

    [Fact]
    public void Les_deux_etapes_se_joignent_independamment()
    {
        var course = DeliveryBuilder.Delivered();

        course.AttacherLaPreuve(EtapeDeLaPreuve.Collecte, Photo, Livreur, DeliveryBuilder.At(13));
        course.AttacherLaPreuve(EtapeDeLaPreuve.Remise, AutrePhoto, Livreur, DeliveryBuilder.At(31));

        course.PickupProofMediaId.Should().Be(Photo);
        course.DeliveryProofMediaId.Should().Be(AutrePhoto);
    }

    [Fact]
    public void Le_controle_avance_dit_la_meme_chose_que_l_attachement()
    {
        // IL EXISTE POUR ETRE APPELE AVANT LE DEPOT, donc il doit refuser
        // EXACTEMENT ce que refuserait l'attachement : un contrôle avancé plus
        // permissif laisserait partir des octets pour rien, un contrôle plus
        // strict refuserait ce qui aurait le droit de passer.
        var course = DeliveryBuilder.Assigned();

        var avance = () => course.EnsurePeutRecevoirLaPreuve(
            EtapeDeLaPreuve.Collecte, Livreur, DeliveryBuilder.At(5));

        avance.Should().Throw<DomainException>().Which.Code.Should().Be("PROOF_STEP_NOT_DONE");

        var collectee = DeliveryBuilder.PickedUp();

        collectee.EnsurePeutRecevoirLaPreuve(EtapeDeLaPreuve.Collecte, Livreur, DeliveryBuilder.At(13))
            .Should().BeNull(because: "la place est libre");

        collectee.AttacherLaPreuve(EtapeDeLaPreuve.Collecte, Photo, Livreur, DeliveryBuilder.At(13));

        collectee.EnsurePeutRecevoirLaPreuve(EtapeDeLaPreuve.Collecte, Livreur, DeliveryBuilder.At(14))
            .Should().Be(Photo, because: "l'appelant doit pouvoir distinguer un rejeu d'un remplacement");
    }
}
