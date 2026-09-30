using FluentAssertions;
using Hba.Billing.Domain.Accounts;
using Hba.Billing.Domain.Exceptions;
using Hba.Billing.Domain.ValueObjects;
using Hba.Billing.Infrastructure.Persistence;
using Hba.Billing.Infrastructure.Persistence.Repositories;
using Hba.BuildingBlocks.Domain;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Hba.Billing.Integration.Tests;

/// <summary>
/// Ce que seule une vraie base peut prouver : les deux unicités et le verrou.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ComptePersistenceTests(PostgresFixture fixture)
{
    private static readonly Actor Finance = Actor.Admin("finance-1");

    private static DateTimeOffset Maintenant => new(2026, 9, 29, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Un_compte_se_relit_a_l_identique()
    {
        var compte = Ouvrir("m-relire");

        await using (var ecriture = fixture.CreateContext())
        {
            ecriture.BillingAccounts.Add(compte);
            await ecriture.SaveChangesAsync(CancellationToken.None);
        }

        await using var lecture = fixture.CreateContext();
        var relu = await lecture.BillingAccounts.FirstAsync(a => a.Id == compte.Id, CancellationToken.None);

        relu.OwnerType.Should().Be("merchant");
        relu.OwnerId.Should().Be("m-relire");
        relu.Balance.Amount.Should().Be(0);
        relu.SettlementMode.Should().Be(SettlementMode.Prepaid);
        relu.Status.Should().Be(AccountStatus.Active);
    }

    /// <summary>
    /// UN SEUL COMPTE PAR TITULAIRE, ET C'EST LA BASE QUI LE TIENT. Le contrôle
    /// de OpenAccountHandler ne suffit pas : deux ouvertures concurrentes le
    /// franchiraient toutes les deux, et deux soldes pour un même commerçant
    /// sont un compte qu'on ne saura plus lire.
    /// </summary>
    [Fact]
    public async Task Deux_comptes_pour_le_meme_titulaire_sont_refuses()
    {
        await using var context = fixture.CreateContext();

        context.BillingAccounts.Add(Ouvrir("m-doublon"));
        await context.SaveChangesAsync(CancellationToken.None);

        context.BillingAccounts.Add(Ouvrir("m-doublon"));

        var acte = async () => await context.SaveChangesAsync(CancellationToken.None);

        await acte.Should().ThrowAsync<DbUpdateException>();
    }

    /// <summary>
    /// VOICI CE QUI INTERDIT REELLEMENT LE DOUBLE DEBIT. Le magasin
    /// d'idempotence évite le rejeu ORDINAIRE ; deux appels strictement
    /// concurrents le franchiraient tous les deux, et c'est cet index-ci qui
    /// rejette le second à l'écriture.
    /// </summary>
    [Fact]
    public async Task Deux_mouvements_de_meme_cle_sont_refuses()
    {
        var compte = Ouvrir("m-cle");
        compte.Credit(MovementKind.Topup, MoneyXof.From(10_000), "recharge", "p-cle-recharge", Finance, Maintenant);

        await using var context = fixture.CreateContext();
        context.BillingAccounts.Add(compte);
        await context.SaveChangesAsync(CancellationToken.None);

        context.AccountMovements.Add(compte.Debit(MoneyXof.From(800), "course-1", "p-doublon", Finance, Maintenant));
        await context.SaveChangesAsync(CancellationToken.None);

        context.AccountMovements.Add(compte.Debit(MoneyXof.From(800), "course-1", "p-doublon", Finance, Maintenant));

        var acte = async () => await context.SaveChangesAsync(CancellationToken.None);

        await acte.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task Une_annulation_ne_s_ecrit_qu_une_fois()
    {
        var compte = Ouvrir("m-annule");
        compte.Credit(MovementKind.Topup, MoneyXof.From(10_000), "recharge", "p-annule-recharge", Finance, Maintenant);
        var debit = compte.Debit(MoneyXof.From(800), "course-2", "p-annule", Finance, Maintenant);

        await using var context = fixture.CreateContext();
        context.BillingAccounts.Add(compte);
        context.AccountMovements.Add(debit);
        await context.SaveChangesAsync(CancellationToken.None);

        context.AccountMovements.Add(compte.ReverseDebit(debit, Finance, Maintenant));
        await context.SaveChangesAsync(CancellationToken.None);

        // LA CLE EST DERIVEE, DONC LA SECONDE TENTATIVE PORTE LA MEME : c'est
        // ce qui rend la compensation rejouable sans rembourser deux fois.
        context.AccountMovements.Add(compte.ReverseDebit(debit, Finance, Maintenant));

        var acte = async () => await context.SaveChangesAsync(CancellationToken.None);

        await acte.Should().ThrowAsync<DbUpdateException>();
    }

    /// <summary>
    /// LE TEST QUI JUSTIFIE TOUT CE PROJET.
    /// </summary>
    ///
    /// <remarks>
    /// Deux courses créées à la même seconde sur le même compte : sans le verrou
    /// de ligne, les deux lisent le solde avant que l'autre ne l'écrive, les
    /// deux passent, et le compte descend sous zéro SANS QU'AUCUNE ERREUR NE
    /// SOIT LEVÉE NULLE PART. C'est la panne qu'on ne voit pas : le solde est
    /// juste un peu faux, et personne ne s'en aperçoit avant le rapprochement.
    ///
    /// LE SOLDE NE PORTE QU'UNE COURSE. On en lance deux ; exactement une doit
    /// aboutir, et l'autre doit être refusée pour SOLDE INSUFFISANT — ce qui
    /// n'arrive que si la seconde a ATTENDU de lire le solde écrit par la
    /// première. Un échec de concurrence optimiste ne compterait pas comme un
    /// succès de ce test : c'est bien le code métier qui doit refuser.
    /// </remarks>
    [Fact]
    public async Task Deux_debits_concurrents_ne_depassent_pas_le_solde()
    {
        var compte = Ouvrir("m-verrou");
        compte.Credit(MovementKind.Topup, MoneyXof.From(1_000), "recharge", "p-verrou-recharge", Finance, Maintenant);

        await using (var amorce = fixture.CreateContext())
        {
            amorce.BillingAccounts.Add(compte);
            await amorce.SaveChangesAsync(CancellationToken.None);
        }

        var premier = DebiterAsync("p-verrou-a", 800);
        var second = DebiterAsync("p-verrou-b", 800);

        var resultats = await Task.WhenAll(premier, second);

        resultats.Count(r => r is null).Should().Be(1, "une seule des deux courses tient dans le solde");

        // PAS DE MOTIF « is » DANS UN PREDICAT FLUENTASSERTIONS : il devient un
        // arbre d'expression, qui n'admet pas la mise en correspondance de type.
        // Le compilateur le refuse (CS8122), et on filtre donc en LINQ ordinaire.
        var refus = resultats.OfType<DomainException>().SingleOrDefault();

        refus.Should().NotBeNull();
        refus!.Code.Should().Be(BillingErrorCodes.InsufficientBalance);

        await using var lecture = fixture.CreateContext();
        var relu = await lecture.BillingAccounts.FirstAsync(a => a.Id == compte.Id, CancellationToken.None);

        relu.Balance.Amount.Should().Be(200);
        relu.Balance.Amount.Should().BeGreaterThanOrEqualTo(0);
    }

    /// <summary>
    /// LE VERROU EXIGE UNE TRANSACTION, ET LE DEPOT LE FAIT SAVOIR.
    /// </summary>
    ///
    /// <remarks>
    /// LE TEST QUI AURAIT DU EXISTER LE PREMIER. « FOR UPDATE » hors transaction
    /// reussit, rend le compte, et ne verrouille rien : PostgreSQL le relache a
    /// la fin de l'instruction. Le defaut etait donc INVISIBLE — c'est ce qui l'a
    /// laisse passer une journee entiere, avec un test du verrou qui ouvrait sa
    /// propre transaction et passait sans rien prouver du service.
    ///
    /// Le depot leve maintenant. Ce test est ce qui empechera de retirer ce garde
    /// en le prenant pour une precaution inutile.
    /// </remarks>
    [Fact]
    public async Task Le_verrou_hors_transaction_est_refuse()
    {
        await using var context = fixture.CreateContext();
        var comptes = new BillingAccountRepository(context);

        var acte = async () =>
            await comptes.GetForUpdateAsync("merchant", "m-sans-transaction", CancellationToken.None);

        // « WithMessage » PLUTOT QU'UN SIMPLE TYPE : InvalidOperationException est
        // levee par beaucoup de choses, et un test qui se contente du type
        // passerait encore si le garde disparaissait et qu'EF levait pour une
        // autre raison.
        (await acte.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*transaction ouverte*");
    }

    /// <summary>
    /// Débite comme le service le fait, et rend l'exception ou null au succès.
    /// </summary>
    ///
    /// <remarks>
    /// PAR « EfTransactionRunner », ET NON PAR UNE TRANSACTION OUVERTE A LA MAIN.
    ///
    /// C'est la correction la plus importante de ce fichier. La version
    /// precedente ouvrait sa propre transaction : elle prouvait que « FOR UPDATE
    /// serialise quand on l'entoure d'une transaction », ce qui est vrai de
    /// PostgreSQL et n'engageait en rien le service — lequel n'en ouvrait
    /// AUCUNE. Le test passait, le plafond etait franchi en production, et le
    /// vert donnait confiance.
    ///
    /// Le test emprunte donc exactement le chemin des gestionnaires : le meme
    /// runner, le meme depot, le meme ordre. S'il cesse de passer, c'est le
    /// service qui a change, pas le montage du test.
    /// </remarks>
    private async Task<Exception?> DebiterAsync(string reference, long montant)
    {
        await using var context = fixture.CreateContext();
        var comptes = new BillingAccountRepository(context);
        var transactions = new EfTransactionRunner(context);

        try
        {
            await transactions.ExecuteAsync(
                async ct =>
                {
                    var compte = await comptes.GetForUpdateAsync("merchant", "m-verrou", ct);

                    // UNE ATTENTE APRES LA PRISE DU VERROU, PAS AVANT. Elle
                    // garantit que les deux taches se chevauchent vraiment : sans
                    // elle, la premiere aurait le temps de valider avant que la
                    // seconde ne commence, et le test passerait meme sans verrou.
                    await Task.Delay(300, ct);

                    var mouvement = compte!.Debit(
                        MoneyXof.From(montant), reference, reference, Finance, Maintenant);

                    context.AccountMovements.Add(mouvement);

                    await context.SaveChangesAsync(ct);

                    return mouvement.Id;
                },
                CancellationToken.None);

            return null;
        }
        catch (Exception exception)
        {
            // PAS DE ROLLBACK ICI : le runner l'a deja fait en sortant par
            // l'exception. Le refaire leverait sur une transaction disposee.
            return exception;
        }
    }

    private static BillingAccount Ouvrir(string ownerId)
        => BillingAccount.Open(Guid.CreateVersion7(), "merchant", ownerId, MoneyXof.From(0), Finance, Maintenant);
}
