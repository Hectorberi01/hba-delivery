using FluentAssertions;
using Hba.BuildingBlocks.Domain;
using Hba.BuildingBlocks.Security;
using Hba.Media.Application.Assets;
using Hba.Media.Domain.Assets;
using Xunit;

namespace Hba.Media.Application.Tests;

/// <summary>
/// La matrice d'accès aux médias.
/// </summary>
///
/// <remarks>
/// CES TESTS NAISSENT DU CONSTAT B3, le 30 septembre 2026 : quatre gestionnaires
/// sur cinq n'avaient AUCUNE vérification d'appelant. Tout porteur d'un jeton
/// valide qui connaissait un identifiant obtenait une URL signée sur la pièce
/// d'identité d'un livreur, ou l'effaçait ; et la liste, qui prend le
/// propriétaire dans la requête, rendait le dossier de n'importe qui.
///
/// CE QUI EST EPROUVE ICI EST UNE FRONTIERE, pas une liste de permissions : trois
/// familles passent — le système, le back-office, le titulaire — et rien d'autre.
/// </remarks>
public sealed class AutorisationDesMediasTests
{
    private static readonly string Client = Guid.CreateVersion7().ToString();
    private static readonly string AutreClient = Guid.CreateVersion7().ToString();
    private static readonly string Livreur = Guid.CreateVersion7().ToString();
    private static readonly string Commerce = Guid.CreateVersion7().ToString();
    private static readonly Guid Media = Guid.CreateVersion7();

    [Fact]
    public void Un_client_lit_sa_propre_photo()
    {
        var acte = () => MediaAccess.EnsureCanReadAsset(
            AppelantFactice.Client(Client), MediaOwnerType.Customer, Client, MediaKind.ProfilePhoto, Media);

        acte.Should().NotThrow();
    }

    [Fact]
    public void Un_client_ne_lit_pas_la_photo_d_un_autre()
    {
        var acte = () => MediaAccess.EnsureCanReadAsset(
            AppelantFactice.Client(Client), MediaOwnerType.Customer, AutreClient, MediaKind.ProfilePhoto, Media);

        // INTROUVABLE ET NON INTERDIT : « interdit » confirmerait que ce média
        // existe, donc à qui il appartient si l'appelant a deviné juste.
        acte.Should().Throw<NotFoundException>();
    }

    [Fact]
    public void Un_client_ne_lit_pas_la_piece_d_identite_d_un_livreur()
    {
        // LE CAS QUI A MOTIVE LE CONSTAT. Il ne demande aucune ruse : un
        // identifiant de média, et la CNI sortait en URL signée.
        var acte = () => MediaAccess.EnsureCanReadAsset(
            AppelantFactice.Client(Client), MediaOwnerType.Driver, Livreur, MediaKind.NationalId, Media);

        acte.Should().Throw<NotFoundException>();
    }

    [Fact]
    public void Un_livreur_ne_lit_pas_la_piece_d_un_autre_livreur()
    {
        var acte = () => MediaAccess.EnsureCanReadAsset(
            AppelantFactice.Livreur(Livreur), MediaOwnerType.Driver, AutreClient, MediaKind.NationalId, Media);

        acte.Should().Throw<NotFoundException>();
    }

    [Fact]
    public void Le_role_doit_accompagner_l_identifiant()
    {
        // UN SUJET QUI CORRESPOND NE SUFFIT PAS. Un jeton dont le sujet vaut
        // l'identifiant du livreur, mais qui ne porte pas le rôle driver, ne doit
        // pas lire la pièce : sans cette condition, un compte client dont le
        // sujet coïnciderait passerait.
        var acte = () => MediaAccess.EnsureCanReadAsset(
            AppelantFactice.Client(Livreur), MediaOwnerType.Driver, Livreur, MediaKind.NationalId, Media);

        acte.Should().Throw<NotFoundException>();
    }

    [Theory]
    [InlineData(HbaRoles.Admin)]
    [InlineData(HbaRoles.Ops)]
    [InlineData(HbaRoles.Support)]
    [InlineData(HbaRoles.Finance)]
    public void Le_back_office_lit_le_dossier_d_un_livreur(string role)
    {
        // LES QUATRE RÔLES DU BACK-OFFICE LISENT CE DOSSIER. La preuve de
        // livraison est la seule pièce qui échappe à cette règle, et les tests
        // qui suivent la cernent.
        var acte = () => MediaAccess.EnsureCanReadAsset(
            AppelantFactice.BackOffice(role), MediaOwnerType.Driver, Livreur, MediaKind.NationalId, Media);

        acte.Should().NotThrow();
    }

    [Fact]
    public void Le_systeme_lit_tout_parce_qu_il_a_deja_autorise()
    {
        // C'EST LE SENS DU POINT 27 : « l'autorisation reste au service
        // propriétaire […] ils demandent ensuite une URL signée à Media, qui ne
        // discute pas ». Media fait confiance au service, pas à l'utilisateur.
        var acte = () => MediaAccess.EnsureCanReadAsset(
            AppelantFactice.Systeme(), MediaOwnerType.Driver, Livreur, MediaKind.NationalId, Media);

        acte.Should().NotThrow();
    }

    [Fact]
    public void Un_anonyme_ne_lit_rien()
    {
        var acte = () => MediaAccess.EnsureCanReadAsset(
            AppelantFactice.Anonyme(), MediaOwnerType.Customer, Client, MediaKind.ProfilePhoto, Media);

        acte.Should().Throw<NotFoundException>();
    }

    [Fact]
    public void Une_preuve_de_livraison_n_a_pas_de_titulaire()
    {
        // UNE COURSE N'A PAS DE COMPTE : personne ne peut « être » ce
        // propriétaire. Le jour où le client devra voir la preuve de SA
        // livraison, c'est Delivery qui l'autorisera puis demandera l'URL avec un
        // jeton de service — pas cette classe.
        var course = Guid.CreateVersion7().ToString();

        MediaAccess.EstLeSien(AppelantFactice.Client(course), MediaOwnerType.Delivery, course)
            .Should().BeFalse();

        MediaAccess.EstLeSien(AppelantFactice.Livreur(course), MediaOwnerType.Delivery, course)
            .Should().BeFalse();

        // L'ADMIN, LUI, Y ACCEDE — et lui seul depuis le 30 septembre 2026.
        var acte = () => MediaAccess.EnsureCanReadAsset(
            AppelantFactice.BackOffice(HbaRoles.Admin), MediaOwnerType.Delivery, course, MediaKind.DeliveryProof, Media);

        acte.Should().NotThrow();
    }

    [Theory]
    [InlineData(HbaRoles.Ops)]
    [InlineData(HbaRoles.Support)]
    [InlineData(HbaRoles.Finance)]
    public void Le_back_office_ordinaire_ne_revoit_pas_une_preuve(string role)
    {
        // LA SEULE EXCEPTION AU TEST PRÉCÉDENT, et elle est tranchée : le
        // point 7, le 30 septembre 2026, garde la preuve un mois et n'en ouvre
        // la relecture qu'à l'admin. Une photo de remise cadre une porte, une
        // cour, parfois quelqu'un qui n'a rien demandé et qui n'est même pas
        // client ; la restreindre au rôle le plus étroit est le prix de la
        // garder.
        //
        // CONSÉQUENCE ASSUMÉE : un agent du support qui traite une réclamation
        // ne verra pas la photo et devra passer par l'admin. Élargir se fait en
        // ajoutant un rôle dans PeutVoirCeGenreDeMedia, et nulle part ailleurs.
        var course = Guid.CreateVersion7().ToString();

        var acte = () => MediaAccess.EnsureCanReadAsset(
            AppelantFactice.BackOffice(role), MediaOwnerType.Delivery, course, MediaKind.DeliveryProof, Media);

        acte.Should().Throw<NotFoundException>();
    }

    [Fact]
    public void Le_systeme_revoit_une_preuve_parce_que_Delivery_a_deja_autorise()
    {
        // SANS CE PASSAGE, LA DÉCISION SERAIT INAPPLICABLE : c'est Delivery qui
        // autorisera un jour un donneur d'ordre à revoir la preuve de SA course,
        // puis demandera l'URL avec un jeton de service. Media ne rediscute pas
        // ce que le service propriétaire a tranché — c'est le point 27.
        var course = Guid.CreateVersion7().ToString();

        var acte = () => MediaAccess.EnsureCanReadAsset(
            AppelantFactice.Systeme("delivery"), MediaOwnerType.Delivery, course, MediaKind.DeliveryProof, Media);

        acte.Should().NotThrow();
    }

    [Theory]
    [InlineData(HbaRoles.Ops)]
    [InlineData(HbaRoles.Support)]
    [InlineData(HbaRoles.Finance)]
    public void La_restriction_vise_la_nature_et_non_le_proprietaire(string role)
    {
        // CE QUI EST FERMÉ, C'EST LA PHOTO, PAS LA COURSE. Le même ops qui ne
        // voit pas la preuve doit continuer à lire la facture de la même course,
        // sans quoi la décision aurait fermé bien plus que ce qui a été demandé.
        var course = Guid.CreateVersion7().ToString();

        var acte = () => MediaAccess.EnsureCanReadAsset(
            AppelantFactice.BackOffice(role), MediaOwnerType.Delivery, course, MediaKind.Invoice, Media);

        acte.Should().NotThrow();
    }

    [Fact]
    public void Aucune_nature_n_ouvre_ce_que_l_appartenance_a_refuse()
    {
        // LA NATURE RESTREINT, ELLE N'OUVRE JAMAIS. Un client à qui l'on
        // refuserait déjà le média ne gagne rien à demander une nature libre, et
        // un titulaire ne gagne rien à ce que la sienne le soit.
        var course = Guid.CreateVersion7().ToString();

        MediaAccess.PeutVoirCeGenreDeMedia(AppelantFactice.Client(Client), MediaKind.ProfilePhoto)
            .Should().BeTrue(because: "la nature ne dit rien de l'appartenance");

        var acte = () => MediaAccess.EnsureCanReadAsset(
            AppelantFactice.Client(Client), MediaOwnerType.Delivery, course, MediaKind.ProfilePhoto, Media);

        acte.Should().Throw<NotFoundException>();
    }

    [Fact]
    public void Un_client_ne_revoit_pas_la_preuve_de_sa_propre_course()
    {
        // ET C'EST VOULU AUJOURD'HUI. Le jour où il le pourra, ce sera Delivery
        // qui l'aura autorisé — cette classe ne sait pas qui a commandé quoi, et
        // le lui apprendre serait lui donner le métier d'un autre service.
        var course = Guid.CreateVersion7().ToString();

        var acte = () => MediaAccess.EnsureCanReadAsset(
            AppelantFactice.Client(Client), MediaOwnerType.Delivery, course, MediaKind.DeliveryProof, Media);

        acte.Should().Throw<NotFoundException>();
    }

    [Fact]
    public void Le_livreur_qui_a_pris_la_photo_ne_la_revoit_pas_non_plus()
    {
        // MÊME RAISON, ET ELLE SURPRENDRA : avoir déposé une pièce n'en fait pas
        // le titulaire. La preuve appartient à la course, et le livreur d'hier
        // n'a plus rien à y voir une fois la course finie.
        var course = Guid.CreateVersion7().ToString();

        var acte = () => MediaAccess.EnsureCanReadAsset(
            AppelantFactice.Livreur(Livreur), MediaOwnerType.Delivery, course, MediaKind.DeliveryProof, Media);

        acte.Should().Throw<NotFoundException>();
    }

    [Fact]
    public void Un_commercant_est_reconnu_par_son_commerce_et_non_par_son_sujet()
    {
        MediaAccess.EstLeSien(AppelantFactice.Commercant(Commerce), MediaOwnerType.Merchant, Commerce)
            .Should().BeTrue();

        MediaAccess.EstLeSien(AppelantFactice.Employe(Commerce), MediaOwnerType.Merchant, Commerce)
            .Should().BeTrue();

        // Le rôle sans le claim : le jeton dit « commerçant » et ne dit pas
        // lequel. Il ne doit rien ouvrir.
        MediaAccess
            .EstLeSien(AppelantFactice.Commercant(Commerce).SansIdentifiant(), MediaOwnerType.Merchant, Commerce)
            .Should().BeFalse();
    }

    [Fact]
    public void La_comparaison_ignore_la_casse_et_la_forme_du_guid()
    {
        // LES DEUX BOUTS NE FORMATENT PAS TOUJOURS PAREIL. Comparer les textes
        // ferait dépendre une autorisation d'une majuscule.
        var id = Guid.CreateVersion7();

        MediaAccess
            .EstLeSien(AppelantFactice.Client(id.ToString("D")), MediaOwnerType.Customer, id.ToString("D").ToUpperInvariant())
            .Should().BeTrue();

        MediaAccess
            .EstLeSien(AppelantFactice.Client(id.ToString("D")), MediaOwnerType.Customer, id.ToString("N"))
            .Should().BeTrue();
    }

    [Fact]
    public void Un_identifiant_vide_ou_illisible_n_ouvre_rien()
    {
        foreach (var ownerId in new string?[] { null, string.Empty, "   ", "pas-un-guid" })
        {
            MediaAccess.EstLeSien(AppelantFactice.Client(Client), MediaOwnerType.Customer, ownerId)
                .Should().BeFalse(because: $"« {ownerId ?? "null"} » ne désigne personne");
        }
    }

    [Fact]
    public void Lister_le_dossier_d_un_autre_est_refuse_franchement()
    {
        // ICI LE REFUS EST FRANC, et la nuance est voulue : l'appelant ne devine
        // pas un identifiant de média, il nomme un propriétaire qu'il connaît
        // déjà. Il n'y a rien à lui cacher.
        var acte = () => MediaAccess.EnsureCanList(
            AppelantFactice.Client(Client), MediaOwnerType.Driver, Livreur);

        acte.Should().Throw<ForbiddenException>();
    }

    [Fact]
    public void Lister_ses_propres_medias_est_permis()
    {
        var acte = () => MediaAccess.EnsureCanList(
            AppelantFactice.Client(Client), MediaOwnerType.Customer, Client);

        acte.Should().NotThrow();
    }

    [Fact]
    public void Le_service_ne_depose_QUE_la_preuve_d_une_course()
    {
        // LA SEULE PORTE D'ECRITURE DU SYSTEME, ouverte le 30 septembre 2026 par
        // la décision « Delivery porte les octets » (point 7, question 4). Elle
        // est étroite exprès : tout ce qui suit doit rester refusé, sans quoi
        // n'importe quel service écrirait une pièce d'identité sous n'importe
        // quel livreur.
        var course = Guid.CreateVersion7().ToString();
        var systeme = AppelantFactice.Systeme("delivery");

        MediaAccess.PeutDeposer(systeme, MediaOwnerType.Delivery, course, MediaKind.DeliveryProof)
            .Should().BeTrue();

        // La bonne nature sous le mauvais propriétaire.
        MediaAccess.PeutDeposer(systeme, MediaOwnerType.Driver, Livreur, MediaKind.DeliveryProof)
            .Should().BeFalse();

        // Le bon propriétaire sous la mauvaise nature : une course ne porte pas
        // de pièce d'identité, et ce serait le chemin par lequel on en écrirait.
        MediaAccess.PeutDeposer(systeme, MediaOwnerType.Delivery, course, MediaKind.NationalId)
            .Should().BeFalse();

        // Et le dossier du livreur reste fermé au système, comme avant.
        MediaAccess.PeutDeposer(systeme, MediaOwnerType.Driver, Livreur, MediaKind.NationalId)
            .Should().BeFalse();

        MediaAccess.PeutDeposer(systeme, MediaOwnerType.Customer, Client, MediaKind.ProfilePhoto)
            .Should().BeFalse();
    }

    [Fact]
    public void Aucun_jeton_d_utilisateur_n_emprunte_la_porte_du_service()
    {
        // CE QUI REND CETTE PORTE SURE : elle s'ouvre sur un couple qu'aucun
        // utilisateur ne peut atteindre. Une course n'a pas de compte, donc
        // EstLeSien rend toujours faux pour elle — le livreur affecté lui-même
        // ne passe pas, et c'est voulu : c'est Delivery qui l'a autorisé, et
        // c'est Delivery qui dépose à sa place.
        var course = Guid.CreateVersion7().ToString();

        MediaAccess
            .PeutDeposer(AppelantFactice.Livreur(Livreur), MediaOwnerType.Delivery, course, MediaKind.DeliveryProof)
            .Should().BeFalse();

        MediaAccess
            .PeutDeposer(AppelantFactice.Client(Client), MediaOwnerType.Delivery, course, MediaKind.DeliveryProof)
            .Should().BeFalse();

        MediaAccess
            .PeutDeposer(AppelantFactice.Anonyme(), MediaOwnerType.Delivery, course, MediaKind.DeliveryProof)
            .Should().BeFalse();
    }

    [Fact]
    public void Le_back_office_depose_comme_avant()
    {
        MediaAccess
            .PeutDeposer(AppelantFactice.BackOffice(HbaRoles.Ops), MediaOwnerType.Driver, Livreur, MediaKind.NationalId)
            .Should().BeTrue();
    }

    [Fact]
    public void On_ne_depose_que_pour_soi()
    {
        MediaAccess
            .PeutDeposer(AppelantFactice.Client(Client), MediaOwnerType.Customer, Client, MediaKind.ProfilePhoto)
            .Should().BeTrue();

        // Sans ce refus, le propriétaire serait un paramètre d'URL : un client
        // déposerait une CNI au nom d'un livreur.
        MediaAccess
            .PeutDeposer(AppelantFactice.Client(Client), MediaOwnerType.Driver, Livreur, MediaKind.NationalId)
            .Should().BeFalse();
    }

    [Fact]
    public void Un_jeton_sans_role_ne_passe_par_aucune_porte()
    {
        var appelant = AppelantFactice.SansRole(Client);

        MediaAccess.EstUnAppelantDeConfiance(appelant).Should().BeFalse();
        MediaAccess.EstLeSien(appelant, MediaOwnerType.Customer, Client).Should().BeFalse();
        MediaAccess.PeutDeposer(appelant, MediaOwnerType.Customer, Client, MediaKind.ProfilePhoto)
            .Should().BeFalse();
    }
}
