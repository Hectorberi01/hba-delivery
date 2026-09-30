using FluentAssertions;
using Hba.BuildingBlocks.Domain;
using Hba.Billing.Domain.Accounts;
using Hba.Billing.Domain.Accounts.Events;
using Hba.Billing.Domain.Exceptions;
using Hba.Billing.Domain.ValueObjects;
using Xunit;

namespace Hba.Billing.Domain.Tests;

/// <summary>
/// La règle unique : solde + plafond ≥ montant. Ces tests existent pour qu'elle
/// reste unique — le jour où quelqu'un ajoutera un chemin pour le postpayé,
/// c'est ici que ça se verra.
/// </summary>
public sealed class RegleDeDebitTests
{
    [Fact]
    public void Un_compte_naît_prépayé_sans_plafond_et_à_zéro()
    {
        var compte = CompteBuilder.Ouvert();

        compte.SettlementMode.Should().Be(SettlementMode.Prepaid);
        compte.CreditLimit.Amount.Should().Be(0);
        compte.Balance.Amount.Should().Be(0);
        compte.Status.Should().Be(AccountStatus.Active);
        compte.DomainEvents.Should().ContainSingle(e => e is BillingAccountOpened);
    }

    [Fact]
    public void Un_compte_prépayé_vide_ne_porte_aucune_course()
    {
        var compte = CompteBuilder.Ouvert();

        var act = () => compte.Debit(MoneyXof.From(500), "course-1", "cle-1", CompteBuilder.Finance, CompteBuilder.At());

        act.Should().Throw<DomainException>()
            .Which.Code.Should().Be(BillingErrorCodes.InsufficientBalance);
    }

    [Fact]
    public void Un_refus_ne_déplace_rien()
    {
        var compte = CompteBuilder.Approvisionne(300);

        var act = () => compte.Debit(MoneyXof.From(500), "course-1", "cle-1", CompteBuilder.Finance, CompteBuilder.At());

        act.Should().Throw<DomainException>();
        compte.Balance.Amount.Should().Be(300);
        compte.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void Le_message_du_refus_dit_ce_qui_manque()
    {
        var compte = CompteBuilder.Approvisionne(300);

        var act = () => compte.Debit(MoneyXof.From(500), "course-1", "cle-1", CompteBuilder.Finance, CompteBuilder.At());

        act.Should().Throw<DomainException>().WithMessage("*200*");
    }

    [Fact]
    public void Le_débit_passe_tant_que_le_solde_suffit()
    {
        var compte = CompteBuilder.Approvisionne(2_000);

        var mouvement = compte.Debit(MoneyXof.From(800), "course-1", "cle-1", CompteBuilder.Finance, CompteBuilder.At());

        compte.Balance.Amount.Should().Be(1_200);
        mouvement.Kind.Should().Be(MovementKind.Debit);
        mouvement.Amount.Amount.Should().Be(-800);
        mouvement.BalanceAfter.Amount.Should().Be(1_200);
        compte.DomainEvents.Should().ContainSingle(e => e is AccountDebited);
    }

    [Fact]
    public void Le_solde_peut_tomber_exactement_à_zéro()
    {
        var compte = CompteBuilder.Approvisionne(800);

        compte.Debit(MoneyXof.From(800), "course-1", "cle-1", CompteBuilder.Finance, CompteBuilder.At());

        compte.Balance.Amount.Should().Be(0);
    }

    [Fact]
    public void Un_plafond_accordé_fait_passer_le_compte_en_postpayé()
    {
        var compte = CompteBuilder.Ouvert();

        compte.GrantCreditLimit(MoneyXof.From(50_000), CompteBuilder.Finance, CompteBuilder.At());

        compte.SettlementMode.Should().Be(SettlementMode.Postpaid);
        compte.DomainEvents.Should().ContainSingle(e => e is CreditLimitGranted);
    }

    [Fact]
    public void En_postpayé_le_solde_descend_sous_zéro_et_cet_encours_est_le_solde()
    {
        var compte = CompteBuilder.Ouvert();
        compte.GrantCreditLimit(MoneyXof.From(50_000), CompteBuilder.Finance, CompteBuilder.At());

        compte.Debit(MoneyXof.From(800), "course-1", "cle-1", CompteBuilder.Finance, CompteBuilder.At());

        compte.Balance.Amount.Should().Be(-800);
        compte.Available.Amount.Should().Be(49_200);
    }

    [Fact]
    public void Le_plafond_ne_se_dépasse_pas_d_un_franc()
    {
        var compte = CompteBuilder.Ouvert();
        compte.GrantCreditLimit(MoneyXof.From(1_000), CompteBuilder.Finance, CompteBuilder.At());

        compte.Debit(MoneyXof.From(1_000), "course-1", "cle-1", CompteBuilder.Finance, CompteBuilder.At());

        var act = () => compte.Debit(MoneyXof.From(1), "course-2", "cle-2", CompteBuilder.Finance, CompteBuilder.At());

        act.Should().Throw<DomainException>()
            .Which.Code.Should().Be(BillingErrorCodes.InsufficientBalance);
    }

    [Fact]
    public void Un_compte_suspendu_ne_se_débite_plus()
    {
        var compte = CompteBuilder.Approvisionne(5_000);
        compte.Suspend(CompteBuilder.Finance, CompteBuilder.At());

        var act = () => compte.Debit(MoneyXof.From(500), "course-1", "cle-1", CompteBuilder.Finance, CompteBuilder.At());

        act.Should().Throw<DomainException>()
            .Which.Code.Should().Be(BillingErrorCodes.AccountSuspended);
    }

    [Fact]
    public void Un_compte_suspendu_se_crédite_encore()
    {
        var compte = CompteBuilder.Approvisionne(100);
        compte.Suspend(CompteBuilder.Finance, CompteBuilder.At());

        compte.Credit(MovementKind.InvoicePayment, MoneyXof.From(5_000), "facture-1", "cle-f1", CompteBuilder.Finance, CompteBuilder.At());

        compte.Balance.Amount.Should().Be(5_100);
    }

    [Fact]
    public void Un_montant_nul_ou_négatif_est_refusé()
    {
        var compte = CompteBuilder.Approvisionne(5_000);

        var zero = () => compte.Debit(MoneyXof.From(0), "course-1", "cle-1", CompteBuilder.Finance, CompteBuilder.At());
        var negatif = () => compte.Debit(MoneyXof.From(-100), "course-2", "cle-2", CompteBuilder.Finance, CompteBuilder.At());

        zero.Should().Throw<DomainException>().Which.Code.Should().Be(BillingErrorCodes.InvalidAmount);
        negatif.Should().Throw<DomainException>().Which.Code.Should().Be(BillingErrorCodes.InvalidAmount);
    }

    [Fact]
    public void Un_débit_ne_passe_pas_par_Credit()
    {
        var compte = CompteBuilder.Approvisionne(5_000);

        var act = () => compte.Credit(MovementKind.Debit, MoneyXof.From(100), "course-1", "cle-1", CompteBuilder.Finance, CompteBuilder.At());

        act.Should().Throw<DomainException>();
    }
}
