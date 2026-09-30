using FluentAssertions;
using Hba.Billing.Domain.Accounts;
using Hba.Billing.Domain.Exceptions;
using Hba.Billing.Domain.ValueObjects;
using Hba.BuildingBlocks.Domain;
using Xunit;

namespace Hba.Billing.Domain.Tests;

/// <summary>
/// L'annulation d'un débit orphelin : Delivery débite avant de créer la course,
/// et doit pouvoir rendre l'argent quand la création échoue.
/// </summary>
public sealed class AnnulationDeDebitTests
{
    [Fact]
    public void Annuler_un_debit_remet_le_solde_ou_il_etait()
    {
        var compte = CompteBuilder.Approvisionne(10_000);
        var debit = compte.Debit(MoneyXof.From(800), "course-1", "course-1", CompteBuilder.Finance, CompteBuilder.At(1));

        compte.Balance.Amount.Should().Be(9_200);

        compte.ReverseDebit(debit, CompteBuilder.Finance, CompteBuilder.At(2));

        compte.Balance.Amount.Should().Be(10_000);
    }

    /// <summary>
    /// LE MONTANT N'EST PAS CHOISI PAR L'APPELANT, et c'est ce qui rend
    /// l'annulation ouvrable au titulaire alors que le crédit ne l'est pas.
    /// </summary>
    [Fact]
    public void Le_remboursement_porte_exactement_le_montant_du_debit()
    {
        var compte = CompteBuilder.Approvisionne(10_000);
        var debit = compte.Debit(MoneyXof.From(1_350), "course-9", "course-9", CompteBuilder.Finance, CompteBuilder.At(1));

        var remboursement = compte.ReverseDebit(debit, CompteBuilder.Finance, CompteBuilder.At(2));

        remboursement.Amount.Amount.Should().Be(1_350);
        remboursement.Kind.Should().Be(MovementKind.Refund);
        remboursement.Reference.Should().Be("course-9");
    }

    /// <summary>
    /// LA CLE SE CALCULE, donc deux tentatives produisent la même clé et
    /// l'unicité en base rejette la seconde. Le domaine ne peut pas l'empêcher
    /// lui-même — il ne voit qu'un mouvement à la fois — mais il doit rendre la
    /// clé PREVISIBLE, sans quoi la base n'a rien à rejeter.
    /// </summary>
    [Fact]
    public void La_cle_de_l_annulation_est_derivee_de_celle_du_debit()
    {
        var compte = CompteBuilder.Approvisionne(10_000);
        var debit = compte.Debit(MoneyXof.From(800), "course-1", "course-1", CompteBuilder.Finance, CompteBuilder.At(1));

        var premier = compte.ReverseDebit(debit, CompteBuilder.Finance, CompteBuilder.At(2));
        var second = compte.ReverseDebit(debit, CompteBuilder.Finance, CompteBuilder.At(3));

        premier.IdempotencyKey.Should().Be("reverse:course-1");
        second.IdempotencyKey.Should().Be(premier.IdempotencyKey);
    }

    [Fact]
    public void On_n_annule_pas_le_debit_d_un_autre_compte()
    {
        var mien = CompteBuilder.Approvisionne(10_000);
        var autre = CompteBuilder.Approvisionne(10_000);
        var debitDeLAutre = autre.Debit(MoneyXof.From(800), "course-2", "course-2", CompteBuilder.Finance, CompteBuilder.At(1));

        var acte = () => mien.ReverseDebit(debitDeLAutre, CompteBuilder.Finance, CompteBuilder.At(2));

        acte.Should().Throw<DomainException>()
            .Which.Code.Should().Be(BillingErrorCodes.MovementNotOnThisAccount);
    }

    [Fact]
    public void On_n_annule_que_des_debits()
    {
        var compte = CompteBuilder.Approvisionne(10_000);
        var recharge = compte.Credit(MovementKind.Topup, MoneyXof.From(500), "r2", "c-r2", CompteBuilder.Finance, CompteBuilder.At(1));

        var acte = () => compte.ReverseDebit(recharge, CompteBuilder.Finance, CompteBuilder.At(2));

        acte.Should().Throw<DomainException>()
            .Which.Code.Should().Be(BillingErrorCodes.NotADebit);
    }

    /// <summary>
    /// UN COMPTE SUSPENDU SE FAIT QUAND MEME REMBOURSER. La suspension interdit
    /// de dépenser ; refuser de rendre l'argent d'une course qui n'a pas eu lieu
    /// ferait payer la suspension au titulaire.
    /// </summary>
    [Fact]
    public void Un_compte_suspendu_se_fait_rembourser()
    {
        var compte = CompteBuilder.Approvisionne(10_000);
        var debit = compte.Debit(MoneyXof.From(800), "course-1", "course-1", CompteBuilder.Finance, CompteBuilder.At(1));
        compte.Suspend(CompteBuilder.Finance, CompteBuilder.At(2));

        var acte = () => compte.ReverseDebit(debit, CompteBuilder.Finance, CompteBuilder.At(3));

        acte.Should().NotThrow();
        compte.Balance.Amount.Should().Be(10_000);
    }
}
