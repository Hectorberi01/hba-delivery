using FluentAssertions;
using Hba.BuildingBlocks.Domain;
using Hba.Billing.Domain.Accounts;
using Hba.Billing.Domain.Accounts.Events;
using Hba.Billing.Domain.Exceptions;
using Hba.Billing.Domain.ValueObjects;
using Xunit;

namespace Hba.Billing.Domain.Tests;

/// <summary>
/// Le journal comptable : signes, traçabilité, et le seuil d'alerte qui ne doit
/// parler qu'une fois.
/// </summary>
public sealed class MouvementsTests
{
    [Fact]
    public void La_somme_des_mouvements_refait_le_solde()
    {
        var compte = CompteBuilder.Ouvert();

        var mouvements = new[]
        {
            compte.Credit(MovementKind.Topup, MoneyXof.From(10_000), "r1", "c1", CompteBuilder.Finance, CompteBuilder.At()),
            compte.Debit(MoneyXof.From(800), "course-1", "c2", CompteBuilder.Finance, CompteBuilder.At(1)),
            compte.Debit(MoneyXof.From(1_200), "course-2", "c3", CompteBuilder.Finance, CompteBuilder.At(2)),
            compte.Credit(MovementKind.Refund, MoneyXof.From(800), "course-1", "c4", CompteBuilder.Finance, CompteBuilder.At(3)),
        };

        // C'EST LE CONTROLE QUI RENDRA UN COMPTE VERIFIABLE EN PRODUCTION, et
        // il tient en une ligne : si cette egalite tombe, le solde ment.
        mouvements.Sum(m => m.Amount.Amount).Should().Be(compte.Balance.Amount);
        compte.Balance.Amount.Should().Be(8_800);
    }

    [Fact]
    public void Chaque_mouvement_fige_le_solde_qui_le_suit()
    {
        var compte = CompteBuilder.Approvisionne(10_000);

        var premier = compte.Debit(MoneyXof.From(800), "course-1", "c1", CompteBuilder.Finance, CompteBuilder.At());
        var second = compte.Debit(MoneyXof.From(200), "course-2", "c2", CompteBuilder.Finance, CompteBuilder.At(1));

        premier.BalanceAfter.Amount.Should().Be(9_200);
        second.BalanceAfter.Amount.Should().Be(9_000);
    }

    [Fact]
    public void Un_mouvement_sans_référence_est_refusé()
    {
        var compte = CompteBuilder.Approvisionne(5_000);

        var act = () => compte.Debit(MoneyXof.From(500), "  ", "cle-1", CompteBuilder.Finance, CompteBuilder.At());

        act.Should().Throw<DomainException>()
            .Which.Code.Should().Be(BillingErrorCodes.MissingReference);
    }

    [Fact]
    public void Un_mouvement_sans_clé_didempotence_est_refusé()
    {
        var compte = CompteBuilder.Approvisionne(5_000);

        var act = () => compte.Debit(MoneyXof.From(500), "course-1", "", CompteBuilder.Finance, CompteBuilder.At());

        act.Should().Throw<DomainException>()
            .Which.Code.Should().Be(BillingErrorCodes.MissingIdempotencyKey);
    }

    [Fact]
    public void Une_erreur_se_corrige_par_un_ajustement_de_sens_inverse()
    {
        var compte = CompteBuilder.Approvisionne(10_000);
        compte.Debit(MoneyXof.From(800), "course-1", "c1", CompteBuilder.Finance, CompteBuilder.At());

        var correction = compte.Adjust(MoneyXof.From(800), "course-1", "c1-correction", CompteBuilder.Finance, CompteBuilder.At(1));

        compte.Balance.Amount.Should().Be(10_000);
        correction.Kind.Should().Be(MovementKind.Adjustment);
        correction.Amount.Amount.Should().Be(800);
    }

    [Fact]
    public void Un_ajustement_négatif_ne_vérifie_pas_le_plafond()
    {
        // Il corrige une écriture passée, il ne dépense rien : le refuser
        // laisserait un compte faux sans moyen de le remettre juste.
        var compte = CompteBuilder.Approvisionne(100);

        compte.Adjust(MoneyXof.From(-500), "correction", "c-corr", CompteBuilder.Finance, CompteBuilder.At());

        compte.Balance.Amount.Should().Be(-400);
    }

    [Fact]
    public void Un_ajustement_nul_est_refusé()
    {
        var compte = CompteBuilder.Approvisionne(5_000);

        var act = () => compte.Adjust(MoneyXof.From(0), "correction", "c-corr", CompteBuilder.Finance, CompteBuilder.At());

        act.Should().Throw<DomainException>()
            .Which.Code.Should().Be(BillingErrorCodes.InvalidAmount);
    }

    [Fact]
    public void Le_seuil_dalerte_ne_parle_quau_franchissement()
    {
        var compte = CompteBuilder.Approvisionne(2_000, seuil: 1_000);

        // Premier débit : on passe de 2000 à 900, la ligne est franchie.
        compte.Debit(MoneyXof.From(1_100), "course-1", "c1", CompteBuilder.Finance, CompteBuilder.At());
        compte.DomainEvents.OfType<LowBalanceReached>().Should().ContainSingle();

        compte.ClearDomainEvents();

        // Second débit : on est DEJA sous le seuil. Rien ne doit repartir.
        compte.Debit(MoneyXof.From(100), "course-2", "c2", CompteBuilder.Finance, CompteBuilder.At(1));
        compte.DomainEvents.OfType<LowBalanceReached>().Should().BeEmpty();
    }

    [Fact]
    public void Une_recharge_qui_repasse_au_dessus_réarme_lalerte()
    {
        var compte = CompteBuilder.Approvisionne(2_000, seuil: 1_000);
        compte.Debit(MoneyXof.From(1_100), "course-1", "c1", CompteBuilder.Finance, CompteBuilder.At());
        compte.Credit(MovementKind.Topup, MoneyXof.From(5_000), "r2", "c2", CompteBuilder.Finance, CompteBuilder.At(1));
        compte.ClearDomainEvents();

        compte.Debit(MoneyXof.From(5_000), "course-2", "c3", CompteBuilder.Finance, CompteBuilder.At(2));

        compte.DomainEvents.OfType<LowBalanceReached>().Should().ContainSingle();
    }
}
