using FluentAssertions;
using Hba.BuildingBlocks.Domain;
using Hba.BuildingBlocks.Security;
using Hba.Media.Application.Assets;
using Hba.Media.Domain.Assets;
using Xunit;

namespace Hba.Media.Application.Tests;

/// <summary>
/// Ce que rend la liste, et ce qu'elle tait.
/// </summary>
///
/// <remarks>
/// LA FICHE SEULE TRAHIT DÉJÀ. Refuser l'URL signée d'une preuve de livraison ne
/// suffit pas si la liste annonce qu'elle existe : elle en donnerait l'heure, la
/// taille et le déposant, c'est-à-dire l'essentiel de ce que la décision du
/// point 7 voulait fermer. Le tri par nature doit donc s'appliquer AUSSI à
/// l'inventaire, et c'est ce que ce fichier éprouve.
///
/// CES TESTS TIENNENT LES DEUX BOUTS : ce qui disparaît pour ops, et ce qui doit
/// rester visible pour lui. Une règle qui n'aurait pas de second bord fermerait
/// plus que ce qui a été demandé.
/// </remarks>
public sealed class ListeDesMediasTests
{
    private static readonly string Course = Guid.CreateVersion7().ToString();

    [Theory]
    [InlineData(HbaRoles.Ops)]
    [InlineData(HbaRoles.Support)]
    [InlineData(HbaRoles.Finance)]
    public async Task La_preuve_ne_parait_pas_dans_la_liste_du_back_office_ordinaire(string role)
    {
        var inventaire = new InventaireFactice()
            .Avec(MediaOwnerType.Delivery, Course, MediaKind.DeliveryProof)
            .Avec(MediaOwnerType.Delivery, Course, MediaKind.Invoice);

        var vues = await Lister(inventaire, AppelantFactice.BackOffice(role));

        vues.Should().ContainSingle()
            .Which.Kind.Should().Be(MediaKind.Invoice);
    }

    [Fact]
    public async Task L_admin_voit_la_preuve_dans_la_liste()
    {
        var inventaire = new InventaireFactice()
            .Avec(MediaOwnerType.Delivery, Course, MediaKind.DeliveryProof)
            .Avec(MediaOwnerType.Delivery, Course, MediaKind.Invoice);

        var vues = await Lister(inventaire, AppelantFactice.BackOffice(HbaRoles.Admin));

        vues.Select(v => v.Kind).Should()
            .BeEquivalentTo(new[] { MediaKind.DeliveryProof, MediaKind.Invoice });
    }

    [Fact]
    public async Task Le_systeme_voit_la_preuve_dans_la_liste()
    {
        // C'EST PAR LÀ QUE DELIVERY PASSERA le jour où il autorisera un donneur
        // d'ordre : il lui faut pouvoir savoir qu'une preuve existe avant d'en
        // demander l'URL.
        var inventaire = new InventaireFactice()
            .Avec(MediaOwnerType.Delivery, Course, MediaKind.DeliveryProof);

        var vues = await Lister(inventaire, AppelantFactice.Systeme("delivery"));

        vues.Should().ContainSingle();
    }

    [Fact]
    public async Task Le_tri_par_nature_ne_touche_pas_au_dossier_d_un_livreur()
    {
        // LE SECOND BORD DE LA RÈGLE. Ops doit continuer à voir tout le dossier
        // d'un livreur : la décision du point 7 ne visait que la photo de remise.
        var livreur = Guid.CreateVersion7().ToString();

        var inventaire = new InventaireFactice()
            .Avec(MediaOwnerType.Driver, livreur, MediaKind.NationalId)
            .Avec(MediaOwnerType.Driver, livreur, MediaKind.DrivingLicence)
            .Avec(MediaOwnerType.Driver, livreur, MediaKind.VehicleRegistration);

        var vues = await Lister(
            inventaire,
            AppelantFactice.BackOffice(HbaRoles.Ops),
            MediaOwnerType.Driver,
            livreur);

        vues.Should().HaveCount(3);
    }

    [Fact]
    public async Task Un_client_ne_liste_pas_le_dossier_d_une_course()
    {
        // LE REFUS TOMBE AVANT LA LECTURE : une course n'a pas de compte, donc
        // personne n'en est le titulaire. Et il est FRANC, non déguisé en
        // absence — l'appelant nomme un propriétaire qu'il connaît déjà.
        var inventaire = new InventaireFactice()
            .Avec(MediaOwnerType.Delivery, Course, MediaKind.DeliveryProof);

        var acte = async () => await Lister(inventaire, AppelantFactice.Client(Course));

        await acte.Should().ThrowAsync<ForbiddenException>();
    }

    private static Task<IReadOnlyList<MediaView>> Lister(
        InventaireFactice inventaire,
        AppelantFactice appelant,
        MediaOwnerType ownerType = MediaOwnerType.Delivery,
        string? ownerId = null)
    {
        var gestionnaire = new ListMediaHandler(inventaire, appelant);

        return gestionnaire.HandleAsync(
            new ListMediaQuery(ownerType, ownerId ?? Course, Kind: null),
            CancellationToken.None);
    }
}
