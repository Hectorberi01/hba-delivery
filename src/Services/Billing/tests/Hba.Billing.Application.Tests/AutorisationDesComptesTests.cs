using FluentAssertions;
using Hba.Billing.Application.Authorization;
using Hba.BuildingBlocks.Domain;
using Hba.BuildingBlocks.Security;
using Xunit;

namespace Hba.Billing.Application.Tests;

/// <summary>
/// La matrice d'autorisation de Billing.
/// </summary>
///
/// <remarks>
/// CES TESTS EXISTENT PARCE QUE LE SERVICE A VÉCU SANS EUX. Il n'avait qu'un
/// [Authorize] de classe, et comme Delivery reporte le jeton de l'utilisateur
/// final, tout porteur de jeton valide pouvait créditer le compte de son choix.
/// Le premier test ci-dessous est exactement cette faille.
/// </remarks>
public sealed class AutorisationDesComptesTests
{
    private const string Merchant = BillingOwnerTypes.Merchant;
    private const string Partner = BillingOwnerTypes.Partner;

    [Fact]
    public void Un_commercant_ne_peut_pas_crediter_son_propre_compte()
    {
        var appelant = AppelantFactice.Commercant("m-1");

        var acte = () => BillingAccess.EnsureCanCredit(appelant);

        acte.Should().Throw<ForbiddenException>();
    }

    [Fact]
    public void Un_partenaire_ne_peut_pas_crediter_son_propre_compte()
    {
        var acte = () => BillingAccess.EnsureCanCredit(AppelantFactice.Partenaire("p-1"));

        acte.Should().Throw<ForbiddenException>();
    }

    [Theory]
    [InlineData(HbaRoles.Ops)]
    [InlineData(HbaRoles.Support)]
    public void Ops_et_support_ne_creditent_pas(string role)
    {
        var acte = () => BillingAccess.EnsureCanCredit(AppelantFactice.BackOffice(role));

        acte.Should().Throw<ForbiddenException>();
    }

    [Theory]
    [InlineData(HbaRoles.Finance)]
    [InlineData(HbaRoles.Admin)]
    public void Finance_et_admin_creditent(string role)
    {
        var acte = () => BillingAccess.EnsureCanCredit(AppelantFactice.BackOffice(role));

        acte.Should().NotThrow();
    }

    [Theory]
    [InlineData(HbaRoles.Finance)]
    [InlineData(HbaRoles.Admin)]
    public void Finance_et_admin_ouvrent_un_compte(string role)
    {
        var acte = () => BillingAccess.EnsureCanOpen(AppelantFactice.BackOffice(role));

        acte.Should().NotThrow();
    }

    [Fact]
    public void Un_commercant_n_ouvre_pas_de_compte()
    {
        var acte = () => BillingAccess.EnsureCanOpen(AppelantFactice.Commercant("m-1"));

        acte.Should().Throw<ForbiddenException>();
    }

    [Fact]
    public void Un_commercant_debite_son_compte()
    {
        var acte = () => BillingAccess.EnsureCanDebit(AppelantFactice.Commercant("m-1"), Merchant, "m-1");

        acte.Should().NotThrow();
    }

    [Fact]
    public void Un_commercant_ne_debite_pas_celui_d_un_autre()
    {
        var acte = () => BillingAccess.EnsureCanDebit(AppelantFactice.Commercant("m-1"), Merchant, "m-2");

        acte.Should().Throw<ForbiddenException>();
    }

    [Fact]
    public void Un_partenaire_debite_son_compte()
    {
        var acte = () => BillingAccess.EnsureCanDebit(AppelantFactice.Partenaire("p-1"), Partner, "p-1");

        acte.Should().NotThrow();
    }

    [Fact]
    public void Un_partenaire_ne_debite_pas_celui_d_un_autre()
    {
        var acte = () => BillingAccess.EnsureCanDebit(AppelantFactice.Partenaire("p-1"), Partner, "p-2");

        acte.Should().Throw<ForbiddenException>();
    }

    /// <summary>
    /// LE COMMERÇANT N'EST PAS LE PARTENAIRE. Le référentiel acteurs interdit de
    /// les confondre, et la confusion serait ici une prise d'argent : le compte
    /// d'un partenaire porte les courses de tous ses commerçants.
    /// </summary>
    [Fact]
    public void Un_commercant_ne_debite_pas_un_compte_de_partenaire_meme_de_meme_identifiant()
    {
        var acte = () => BillingAccess.EnsureCanDebit(AppelantFactice.Commercant("x"), Partner, "x");

        acte.Should().Throw<ForbiddenException>();
    }

    [Fact]
    public void Un_partenaire_ne_debite_pas_un_compte_de_commercant_meme_de_meme_identifiant()
    {
        var acte = () => BillingAccess.EnsureCanDebit(AppelantFactice.Partenaire("x"), Merchant, "x");

        acte.Should().Throw<ForbiddenException>();
    }

    [Fact]
    public void Un_employe_de_commercant_ne_debite_rien()
    {
        var acte = () => BillingAccess.EnsureCanDebit(AppelantFactice.Employe("m-1"), Merchant, "m-1");

        acte.Should().Throw<ForbiddenException>();
    }

    [Theory]
    [InlineData(HbaRoles.Finance)]
    [InlineData(HbaRoles.Admin)]
    [InlineData(HbaRoles.Ops)]
    [InlineData(HbaRoles.Support)]
    public void Le_back_office_ne_debite_pas(string role)
    {
        var acte = () => BillingAccess.EnsureCanDebit(AppelantFactice.BackOffice(role), Merchant, "m-1");

        acte.Should().Throw<ForbiddenException>();
    }

    [Fact]
    public void Un_client_particulier_n_a_pas_de_compte_a_debiter()
    {
        var acte = () => BillingAccess.EnsureCanDebit(AppelantFactice.Client(), Merchant, "m-1");

        acte.Should().Throw<ForbiddenException>();
    }

    [Fact]
    public void Un_livreur_ne_debite_rien()
    {
        var acte = () => BillingAccess.EnsureCanDebit(AppelantFactice.Livreur(), Partner, "p-1");

        acte.Should().Throw<ForbiddenException>();
    }

    /// <summary>
    /// UN RÔLE SANS SON CLAIM NE DONNE RIEN. Un jeton commerçant sans
    /// merchant_id ne doit pas devenir titulaire d'un compte au hasard : la
    /// comparaison « null == null » aurait autorisé un débit sur un compte dont
    /// l'identifiant est vide.
    /// </summary>
    [Fact]
    public void Un_role_sans_son_claim_ne_debite_rien()
    {
        var acte = () => BillingAccess.EnsureCanDebit(
            AppelantFactice.Commercant("m-1").SansIdentifiant(), Merchant, string.Empty);

        acte.Should().Throw<ForbiddenException>();
    }

    /// <summary>
    /// LE TEST QUI EXISTE PARCE QUE LA REGLE A ETE FAUSSE UNE FOIS.
    /// </summary>
    ///
    /// <remarks>
    /// L'annulation a ete ouverte au titulaire le 29 septembre 2026, sur
    /// l'argument « il ne peut que recuperer son propre argent ». La cle du debit
    /// EST l'identifiant de la course, rendu au donneur d'ordre a la creation :
    /// il se faisait rembourser une course en cours et la gardait. Ces deux tests
    /// sont ce qui empechera de rouvrir la porte en croyant corriger un oubli.
    /// </remarks>
    [Fact]
    public void Un_commercant_n_annule_pas_son_propre_debit()
    {
        var acte = () => BillingAccess.EnsureCanReverse(AppelantFactice.Commercant("m-1"), Merchant, "m-1");

        acte.Should().Throw<ForbiddenException>();
    }

    [Fact]
    public void Un_partenaire_n_annule_pas_son_propre_debit()
    {
        var acte = () => BillingAccess.EnsureCanReverse(AppelantFactice.Partenaire("p-1"), Partner, "p-1");

        acte.Should().Throw<ForbiddenException>();
    }

    /// <summary>
    /// LE SYSTEME ANNULE, PARCE QUE LUI SEUL SAIT QUE LA COURSE N'EXISTE PAS.
    /// Delivery force son jeton de service sur cet appel plutot que de reporter
    /// celui du donneur d'ordre.
    /// </summary>
    [Fact]
    public void Le_systeme_annule_un_debit()
    {
        var acte = () => BillingAccess.EnsureCanReverse(AppelantFactice.Systeme(), Merchant, "m-1");

        acte.Should().NotThrow();
    }

    [Theory]
    [InlineData(HbaRoles.Finance)]
    [InlineData(HbaRoles.Admin)]
    public void Finance_et_admin_annulent_un_debit(string role)
    {
        var acte = () => BillingAccess.EnsureCanReverse(AppelantFactice.BackOffice(role), Merchant, "m-1");

        acte.Should().NotThrow();
    }

    [Theory]
    [InlineData(HbaRoles.Ops)]
    [InlineData(HbaRoles.Support)]
    public void Ops_et_support_n_annulent_pas(string role)
    {
        var acte = () => BillingAccess.EnsureCanReverse(AppelantFactice.BackOffice(role), Merchant, "m-1");

        acte.Should().Throw<ForbiddenException>();
    }

    [Fact]
    public void Un_client_et_un_livreur_n_annulent_rien()
    {
        FluentActions.Invoking(() => BillingAccess.EnsureCanReverse(AppelantFactice.Client(), Merchant, "m-1"))
            .Should().Throw<ForbiddenException>();
        FluentActions.Invoking(() => BillingAccess.EnsureCanReverse(AppelantFactice.Livreur(), Partner, "p-1"))
            .Should().Throw<ForbiddenException>();
    }

    /// <summary>
    /// LE ROLE « service » N'EST PAS UN PASSE-PARTOUT, et c'est ce test qui le
    /// tient. Il annule un debit ; il ne credite pas, il n'ouvre pas de compte, et
    /// il ne debite pas au nom de quelqu'un.
    /// </summary>
    [Fact]
    public void Le_systeme_ne_peut_rien_d_autre()
    {
        var systeme = AppelantFactice.Systeme();

        FluentActions.Invoking(() => BillingAccess.EnsureCanCredit(systeme))
            .Should().Throw<ForbiddenException>();
        FluentActions.Invoking(() => BillingAccess.EnsureCanOpen(systeme))
            .Should().Throw<ForbiddenException>();
        FluentActions.Invoking(() => BillingAccess.EnsureCanDebit(systeme, Merchant, "m-1"))
            .Should().Throw<ForbiddenException>();
        FluentActions.Invoking(() => BillingAccess.EnsureCanRead(systeme, Merchant, "m-1"))
            .Should().Throw<NotFoundException>();
    }

    [Fact]
    public void Un_appel_non_authentifie_est_refuse()
    {
        var anonyme = AppelantFactice.Anonyme();

        FluentActions.Invoking(() => BillingAccess.EnsureCanDebit(anonyme, Merchant, "m-1"))
            .Should().Throw<ForbiddenException>();
        FluentActions.Invoking(() => BillingAccess.EnsureCanCredit(anonyme))
            .Should().Throw<ForbiddenException>();
        FluentActions.Invoking(() => BillingAccess.EnsureCanOpen(anonyme))
            .Should().Throw<ForbiddenException>();
        FluentActions.Invoking(() => BillingAccess.EnsureCanRead(anonyme, Merchant, "m-1"))
            .Should().Throw<ForbiddenException>();
        FluentActions.Invoking(() => BillingAccess.EnsureCanReverse(anonyme, Merchant, "m-1"))
            .Should().Throw<ForbiddenException>();
    }

    [Fact]
    public void Le_titulaire_lit_son_compte()
    {
        FluentActions.Invoking(() => BillingAccess.EnsureCanRead(AppelantFactice.Commercant("m-1"), Merchant, "m-1"))
            .Should().NotThrow();
        FluentActions.Invoking(() => BillingAccess.EnsureCanRead(AppelantFactice.Partenaire("p-1"), Partner, "p-1"))
            .Should().NotThrow();
    }

    [Theory]
    [InlineData(HbaRoles.Finance)]
    [InlineData(HbaRoles.Admin)]
    [InlineData(HbaRoles.Ops)]
    [InlineData(HbaRoles.Support)]
    public void Tout_le_back_office_lit(string role)
    {
        var acte = () => BillingAccess.EnsureCanRead(AppelantFactice.BackOffice(role), Merchant, "m-1");

        acte.Should().NotThrow();
    }

    /// <summary>
    /// HORS PÉRIMÈTRE, LA LECTURE RÉPOND « INTROUVABLE ». Distinguer l'interdit
    /// de l'absent permettrait d'énumérer les commerçants qui ont un compte.
    /// </summary>
    [Fact]
    public void La_lecture_hors_perimetre_repond_introuvable()
    {
        var acte = () => BillingAccess.EnsureCanRead(AppelantFactice.Commercant("m-1"), Merchant, "m-2");

        acte.Should().Throw<NotFoundException>();
    }

    [Fact]
    public void Un_type_de_titulaire_inconnu_n_autorise_personne()
    {
        FluentActions.Invoking(() => BillingAccess.EnsureCanDebit(AppelantFactice.Commercant("m-1"), "driver", "m-1"))
            .Should().Throw<ForbiddenException>();
        FluentActions.Invoking(() => BillingAccess.EnsureCanRead(AppelantFactice.Commercant("m-1"), "driver", "m-1"))
            .Should().Throw<NotFoundException>();
    }
}
