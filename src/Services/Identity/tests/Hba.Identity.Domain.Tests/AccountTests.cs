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
}
