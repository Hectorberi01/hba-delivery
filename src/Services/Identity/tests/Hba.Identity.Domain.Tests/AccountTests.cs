using FluentAssertions;
using Hba.BuildingBlocks.Domain;
using Hba.Identity.Domain.Accounts;
using Hba.Identity.Domain.Accounts.Events;
using Hba.Identity.Domain.ValueObjects;
using Xunit;

namespace Hba.Identity.Domain.Tests;

public sealed class AccountTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 21, 10, 0, 0, TimeSpan.Zero);
    private static readonly Actor Admin = Actor.Admin("admin-1");

    private static Account NewCustomer() => Account.RegisterWithPhone(
        Guid.CreateVersion7(),
        PhoneNumber.Create("+22997000001"),
        "Hector",
        Roles.Customer,
        Now);

    [Fact]
    public void Une_inscription_par_SMS_ne_crée_que_customer_ou_driver()
    {
        var act = () => Account.RegisterWithPhone(
            Guid.CreateVersion7(),
            PhoneNumber.Create("+22997000001"),
            "Quelqu'un",
            Roles.Admin,
            Now);

        act.Should().Throw<ForbiddenException>(
            "posséder une carte SIM ne doit pas donner accès au back-office");
    }

    [Fact]
    public void Une_inscription_publie_son_événement()
    {
        var account = NewCustomer();

        account.Roles.Should().Equal(Roles.Customer);
        account.Status.Should().Be(AccountStatus.Active);
        account.DomainEvents.Should().ContainSingle(e => e is AccountRegistered);
    }

    [Fact]
    public void Un_compte_sans_rôle_à_mot_de_passe_ne_peut_pas_en_avoir_un()
    {
        var account = NewCustomer();

        var act = () => account.SetPassword(
            PasswordHash.FromPlainText("un-mot-de-passe-correct"),
            Admin,
            Now);

        act.Should().Throw<ForbiddenException>();
    }

    [Fact]
    public void Cinq_échecs_consécutifs_verrouillent_le_compte()
    {
        var account = Account.CreateBackOffice(
            Guid.CreateVersion7(),
            EmailAddress.Create("ops@hbatechettrade.com"),
            "Ops",
            [Roles.Ops],
            PasswordHash.FromPlainText("un-mot-de-passe-correct"),
            Admin,
            Now);

        for (var attempt = 0; attempt < 5; attempt++)
        {
            account.TryPassword("faux", Now).Should().BeFalse();
        }

        account.IsLocked(Now).Should().BeTrue();

        // Même le bon mot de passe est refusé pendant le verrouillage.
        var act = () => account.TryPassword("un-mot-de-passe-correct", Now);
        act.Should().Throw<DomainException>().Which.Code.Should().Be("ACCOUNT_LOCKED");

        account.TryPassword("un-mot-de-passe-correct", Now.AddMinutes(16)).Should().BeTrue();
    }

    [Fact]
    public void Un_compte_suspendu_n_obtient_plus_de_jeton()
    {
        var account = NewCustomer();
        account.Suspend("fraude constatée", Admin, Now);

        var act = account.EnsureCanAuthenticate;

        act.Should().Throw<ForbiddenException>();
        account.DomainEvents.Should().Contain(e => e is AccountStatusChanged);
    }

    [Fact]
    public void Le_rôle_partner_n_appartient_pas_à_un_compte_de_personne()
    {
        var account = NewCustomer();

        var act = () => account.SetRoles([Roles.Partner], "test", Admin, Now);

        act.Should().Throw<ForbiddenException>();
    }

    /// <summary>
    /// CE RÔLE NE SE POSE SUR PERSONNE, et l'oubli était un effet de bord de sa
    /// propre création : ajouté à Roles.All le 30 septembre 2026 pour que
    /// IssueServiceToken puisse l'émettre, il devenait du même coup attribuable
    /// par SetRoles, qui accepte tout ce qui est dans All.
    ///
    /// Ce qu'il ouvre n'est pas un droit de plus mais le contournement de tous
    /// les autres : EnsureCanReverse l'accepte là où il refuse le titulaire du
    /// compte, et DeleteOwnerMedia lui confie l'effacement de pièces d'identité.
    /// </summary>
    [Fact]
    public void Le_rôle_service_n_appartient_pas_à_un_compte_de_personne()
    {
        var account = NewCustomer();

        var act = () => account.SetRoles([Roles.Service], "test", Admin, Now);

        act.Should().Throw<ForbiddenException>();
    }

    [Fact]
    public void Le_rôle_service_ne_se_glisse_pas_à_côté_d_un_rôle_légitime()
    {
        var account = NewCustomer();

        // LE REFUS PORTE SUR L'ENSEMBLE DEMANDÉ, pas sur un rôle isolé : la
        // façon naturelle de s'octroyer « service » est de le demander EN PLUS
        // de ce qu'on a déjà, pour que la requête ait l'air d'une correction
        // anodine.
        var act = () => account.SetRoles([Roles.Customer, Roles.Service], "test", Admin, Now);

        act.Should().Throw<ForbiddenException>();
        account.Roles.Should().NotContain(Roles.Service);
    }

    /// <summary>
    /// Les trois autres portes étaient déjà fermées, et ce test les tient
    /// fermées : la vérification de numéro n'ouvre que customer et driver, un
    /// compte de back-office n'accepte que les quatre rôles du back-office, et
    /// un compte de commerçant que les deux siens. « service » n'est dans aucune
    /// de ces listes.
    /// </summary>
    [Fact]
    public void Aucune_création_de_compte_n_accepte_le_rôle_service()
    {
        Roles.SelfServiceByOtp.Should().NotContain(Roles.Service);
        Roles.BackOffice.Should().NotContain(Roles.Service);
        Roles.Merchant.Should().NotContain(Roles.Service);
        Roles.PasswordBased.Should().NotContain(Roles.Service);
    }

    [Fact]
    public void Un_rôle_de_commerçant_exige_un_commerçant()
    {
        var account = NewCustomer();

        var act = () => account.SetRoles([Roles.MerchantOwner], "promotion", Admin, Now);

        act.Should().Throw<DomainException>().Which.Code.Should().Be("MISSING_MERCHANT");
    }

    [Fact]
    public void Un_rôle_inconnu_est_refusé()
    {
        var account = NewCustomer();

        var act = () => account.SetRoles(["super_admin"], "test", Admin, Now);

        act.Should().Throw<DomainException>().Which.Code.Should().Be("UNKNOWN_ROLE");
    }

    [Fact]
    public void Un_compte_livreur_ne_se_rattache_qu_à_un_seul_profil()
    {
        var account = Account.RegisterWithPhone(
            Guid.CreateVersion7(),
            PhoneNumber.Create("+22997000002"),
            "Koffi",
            Roles.Driver,
            Now);

        account.LinkDriverProfile("driver-1", Admin, Now);
        account.LinkDriverProfile("driver-1", Admin, Now); // idempotent

        var act = () => account.LinkDriverProfile("driver-2", Admin, Now);

        act.Should().Throw<DomainException>().Which.Code.Should().Be("DRIVER_ALREADY_LINKED");
    }

    // ------------------------------------ Suppression par le titulaire ---
    //
    // CE QUI EST TESTE ICI, C'EST CE QUI EMPECHE D'EFFACER TROP TOT. Le reste
    // se relit dans la base ; l'echeance, elle, est la seule chose qui separe
    // un client qui a change d'avis d'un compte perdu pour de bon — et une
    // fois la ligne partie, plus rien ne temoigne de l'erreur.

    private static Actor Lui(Account account) => Actor.Customer(account.Id.ToString());

    [Fact]
    public void Demander_la_suppression_met_le_compte_en_sursis()
    {
        var account = NewCustomer();

        account.RequestDeletion(Now.AddDays(30), Lui(account), Now);

        account.Status.Should().Be(AccountStatus.PendingDeletion);
        account.DeletionScheduledFor.Should().Be(Now.AddDays(30));
        account.DomainEvents.Should().Contain(e => e is AccountDeletionRequested);
    }

    [Fact]
    public void Un_compte_en_sursis_peut_encore_se_connecter()
    {
        var account = NewCustomer();
        account.RequestDeletion(Now.AddDays(30), Lui(account), Now);

        var act = account.EnsureCanAuthenticate;

        act.Should().NotThrow(
            "se reconnecter est precisement le geste par lequel on annule");
    }

    [Fact]
    public void Un_compte_suspendu_ne_se_supprime_pas_lui_même()
    {
        var account = NewCustomer();
        account.Suspend("Signalement en cours.", Admin, Now);

        var act = () => account.RequestDeletion(Now.AddDays(30), Lui(account), Now);

        act.Should().Throw<ForbiddenException>(
            "l'effacement ne doit pas etre une issue pour qui fait l'objet d'une instruction");
    }

    [Fact]
    public void Demander_deux_fois_ne_repousse_pas_l_échéance()
    {
        var account = NewCustomer();
        account.RequestDeletion(Now.AddDays(30), Lui(account), Now);

        account.RequestDeletion(Now.AddDays(90), Lui(account), Now.AddDays(1));

        account.DeletionScheduledFor.Should().Be(
            Now.AddDays(30),
            "un client qui appuie deux fois croirait confirmer, pas repousser");
    }

    [Fact]
    public void Annuler_rend_le_compte_actif_et_oublie_l_échéance()
    {
        var account = NewCustomer();
        account.RequestDeletion(Now.AddDays(30), Lui(account), Now);

        account.CancelDeletion(Lui(account), Now.AddDays(2));

        account.Status.Should().Be(AccountStatus.Active);
        account.DeletionScheduledFor.Should().BeNull();
        account.DeletionRequestedAt.Should().BeNull();
    }

    [Fact]
    public void On_n_efface_pas_avant_l_échéance()
    {
        var account = NewCustomer();
        account.RequestDeletion(Now.AddDays(30), Lui(account), Now);

        var act = () => account.MarkErased(Actor.Scheduler, Now.AddDays(29));

        act.Should().Throw<DomainException>()
            .Which.Code.Should().Be("DELETION_NOT_DUE");
    }

    [Fact]
    public void On_n_efface_pas_un_compte_qui_n_a_rien_demandé()
    {
        var account = NewCustomer();

        var act = () => account.MarkErased(Actor.Scheduler, Now.AddDays(365));

        act.Should().Throw<DomainException>()
            .Which.Code.Should().Be("DELETION_NOT_REQUESTED");
    }

    [Fact]
    public void L_échéance_atteinte_lève_l_événement_que_les_autres_services_attendent()
    {
        var account = NewCustomer();
        account.RequestDeletion(Now.AddDays(30), Lui(account), Now);
        account.ClearDomainEvents();

        account.MarkErased(Actor.Scheduler, Now.AddDays(30));

        account.DomainEvents.Should().ContainSingle(e => e is AccountErased);
    }
}
