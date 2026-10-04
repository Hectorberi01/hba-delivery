using FluentAssertions;
using Hba.BuildingBlocks.Domain;
using Hba.Driver.Domain.Drivers;
using Hba.Driver.Domain.Exceptions;
using Xunit;

namespace Hba.Driver.Domain.Tests;

/// <summary>
/// Ce qu'un livreur doit fournir, selon ce qu'il conduit.
/// </summary>
///
/// <remarks>
/// POURQUOI CE FICHIER EXISTE, ET POURQUOI IL ARRIVE EN RETARD. Le service
/// Driver n'avait AUCUN projet de tests — le seul des dix a en etre depourvu —
/// alors qu'il porte la regle qui decide si un livreur peut travailler. Les
/// pieces requises ont ete rendues variables le 30 septembre 2026, pour ouvrir
/// le velo ; cette regle n'a jamais ete eprouvee, et c'est elle qu'on eprouve
/// ici avant d'y toucher de nouveau.
///
/// CE QUI SE JOUE N'EST PAS UNE LISTE, C'EST UN GUICHET. Exiger d'un cycliste
/// un permis et une carte grise ne le retarde pas : cela le ferme pour
/// toujours, puisqu'il ne pourra jamais les produire. Se tromper dans l'autre
/// sens laisse rouler un motocycliste sans permis.
/// </remarks>
public sealed class DossierDuLivreurTests
{
    // ActorKind N'A PAS DE VALEUR « Ops ». Le referentiel distingue les roles
    // (ops, support, finance) de l'ACTEUR d'audit, qui les regroupe sous Admin :
    // ce qui est trace est « le back-office a agi », pas lequel de ses guichets.
    private static readonly Actor Ops = Actor.Human(ActorKind.Admin, "ops-1");
    private static readonly DateTimeOffset Maintenant =
        new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    private static DriverAggregate Livreur(VehicleType type, string plaque = "AB-1234-RB")
        => DriverAggregate.Register(
            Guid.CreateVersion7(),
            "Kofi",
            "+22997000000",
            Vehicle.Create(type, plaque, 20_000),
            Ops,
            Maintenant);

    private static DriverDocument Piece(DocumentType type)
        => DriverDocument.Create(type, $"cle/{type}", "image/jpeg", 1024, Maintenant);

    // ---- Les pieces exigees --------------------------------------------------

    [Theory]
    [InlineData(VehicleType.Motorcycle)]
    [InlineData(VehicleType.Car)]
    [InlineData(VehicleType.Van)]
    [InlineData(VehicleType.Tricycle)]
    public void Un_vehicule_a_moteur_exige_les_cinq_pieces(VehicleType type)
    {
        // LE TRICYCLE EST DANS CETTE LISTE, ET C'EST LE CAS QUI SURPREND. A
        // Cotonou il en existe des motorises comme des pedales ; le referentiel
        // ne distingue pas les deux, donc le domaine traite le type entier
        // comme motorise. C'est le choix prudent — il demande des papiers a
        // quelqu'un qui n'en a peut-etre pas besoin, plutot que d'en dispenser
        // quelqu'un qui en a besoin.
        DriverAggregate.PiecesRequisesPour(type).Should().BeEquivalentTo(
            DriverAggregate.PiecesDUnVehiculeAMoteur);
    }

    [Fact]
    public void Un_velo_exige_trois_pieces_et_aucun_papier_de_vehicule()
    {
        var requises = DriverAggregate.PiecesRequisesPour(VehicleType.Bicycle);

        requises.Should().BeEquivalentTo(new[]
        {
            DocumentType.NationalId,
            DocumentType.IdentityPhoto,
            DocumentType.VehiclePhoto,
        });

        // LES DEUX ABSENCES SONT LE SUJET, PAS LES TROIS PRESENCES. Un velo n'a
        // ni permis ni carte grise, et les exiger fermait ce type de livreur
        // avant meme de l'ouvrir.
        requises.Should().NotContain(DocumentType.DrivingLicence);
        requises.Should().NotContain(DocumentType.VehicleRegistration);
    }

    [Fact]
    public void Les_pieces_manquantes_suivent_le_vehicule_declare()
    {
        var cycliste = Livreur(VehicleType.Bicycle, plaque: string.Empty);

        cycliste.PiecesManquantes.Should().HaveCount(3);

        cycliste.AttachDocument(Piece(DocumentType.NationalId), Ops, Maintenant);
        cycliste.AttachDocument(Piece(DocumentType.IdentityPhoto), Ops, Maintenant);
        cycliste.AttachDocument(Piece(DocumentType.VehiclePhoto), Ops, Maintenant);

        cycliste.PiecesManquantes.Should().BeEmpty();
    }

    // ---- La plaque -----------------------------------------------------------

    [Fact]
    public void Seul_le_velo_roule_sans_plaque()
    {
        Vehicle.SansPlaque.Should().BeEquivalentTo(new[] { VehicleType.Bicycle });
    }

    [Fact]
    public void Un_velo_sans_plaque_est_declare()
    {
        Vehicle.Create(VehicleType.Bicycle, null, 5_000).EstDeclare.Should().BeTrue();
    }

    [Fact]
    public void Une_moto_sans_plaque_n_est_pas_declaree()
    {
        Vehicle.Create(VehicleType.Motorcycle, null, 20_000).EstDeclare.Should().BeFalse();
    }

    [Fact]
    public void Le_vehicule_inconnu_n_est_pas_declare()
    {
        // CE TEST TIENT TOUTE LA SURETE DE « EstDeclare », et il merite qu'on
        // dise pourquoi. Vehicle.Unknown rend une MOTO sans plaque : c'est ce
        // qui fait qu'un livreur tout juste inscrit, qui n'a rien declare, est
        // considere comme NON declare. Si Unknown rendait un velo — un type qui
        // n'exige pas de plaque — tout nouveau livreur serait declare des la
        // seconde de son inscription, et passerait le guichet sans avoir rien
        // dit de son vehicule.
        Vehicle.Unknown.EstDeclare.Should().BeFalse();
    }

    [Fact]
    public void Declarer_un_velo_sans_plaque_est_accepte()
    {
        var livreur = Livreur(VehicleType.Motorcycle);

        var action = () => livreur.DeclareVehicle(
            Vehicle.Create(VehicleType.Bicycle, null, 5_000), Ops, Maintenant);

        action.Should().NotThrow();
        livreur.VehiculeDeclare.Should().BeTrue();
    }

    [Fact]
    public void Declarer_une_voiture_sans_plaque_est_refuse()
    {
        var livreur = Livreur(VehicleType.Motorcycle);

        var action = () => livreur.DeclareVehicle(
            Vehicle.Create(VehicleType.Car, "   ", 200_000), Ops, Maintenant);

        action.Should().Throw<DomainException>()
            .Which.Code.Should().Be(DriverErrorCodes.MissingVehiclePlate);
    }

    // ---- Le guichet ----------------------------------------------------------

    [Fact]
    public void Un_cycliste_complet_peut_soumettre_son_dossier()
    {
        var cycliste = Livreur(VehicleType.Bicycle, plaque: string.Empty);

        cycliste.AttachDocument(Piece(DocumentType.NationalId), Ops, Maintenant);
        cycliste.AttachDocument(Piece(DocumentType.IdentityPhoto), Ops, Maintenant);
        cycliste.AttachDocument(Piece(DocumentType.VehiclePhoto), Ops, Maintenant);

        var action = () => cycliste.SubmitForReview(Ops, Maintenant);

        // SANS LA CORRECTION DU 30 SEPTEMBRE, CE TEST ECHOUAIT DEUX FOIS : le
        // dossier reclamait un permis et une carte grise qu'un cycliste ne peut
        // pas produire, et le controle de plaque le retenait au guichet pour
        // toujours.
        action.Should().NotThrow();
    }

    [Fact]
    public void Un_dossier_incomplet_est_refuse_en_nommant_ce_qui_manque()
    {
        var livreur = Livreur(VehicleType.Motorcycle);
        livreur.AttachDocument(Piece(DocumentType.NationalId), Ops, Maintenant);

        var action = () => livreur.SubmitForReview(Ops, Maintenant);

        action.Should().Throw<DomainException>()
            .Which.Code.Should().Be(DriverErrorCodes.IncompleteApplication);
    }

    [Fact]
    public void Un_dossier_complet_mais_sans_vehicule_declare_est_refuse()
    {
        // LE CAS QUE « PiecesManquantes » NE VOIT PAS. Un motocycliste peut
        // deposer ses cinq pieces sans avoir jamais appele DeclareVehicle : son
        // vehicule reste Unknown, donc une moto sans plaque. Les pieces sont
        // completes et le dossier n'est pourtant pas soumettable.
        var livreur = DriverAggregate.Register(
            Guid.CreateVersion7(),
            "Kofi",
            "+22997000000",
            Vehicle.Unknown,
            Ops,
            Maintenant);

        foreach (var type in DriverAggregate.PiecesDUnVehiculeAMoteur)
        {
            livreur.AttachDocument(Piece(type), Ops, Maintenant);
        }

        livreur.PiecesManquantes.Should().BeEmpty();
        livreur.VehiculeDeclare.Should().BeFalse();

        var action = () => livreur.SubmitForReview(Ops, Maintenant);

        action.Should().Throw<DomainException>();
    }
}
