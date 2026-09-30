using FluentAssertions;
using Hba.Billing.Application.IntegrationEvents;
using Hba.Billing.Domain.Accounts;
using Hba.Billing.Domain.ValueObjects;
using Hba.BuildingBlocks.Domain;
using Hba.Billing.Infrastructure.Persistence;
using Hba.BuildingBlocks.Messaging.Kafka;
using Hba.BuildingBlocks.Messaging.Outbox;
using Hba.Contracts.Billing.V1;
using Microsoft.EntityFrameworkCore;
using Xunit;

// « MovementKind » EXISTE DANS LES DEUX MONDES : l'enumeration du domaine et
// celle du proto portent le meme nom, ce qui est voulu — c'est la meme notion.
// Ce fichier importe les deux namespaces parce qu'il ecrit un mouvement puis
// relit le message publie ; sans cet alias, chaque mention serait ambigue.
using DomainKind = Hba.Billing.Domain.Accounts.MovementKind;

namespace Hba.Billing.Integration.Tests;

/// <summary>
/// L'alerte de solde : du fait de domaine jusqu'à la ligne d'Outbox.
/// </summary>
///
/// <remarks>
/// LE FAIT ÉTAIT LEVÉ ET TESTÉ, ET IL NE SORTAIT NULLE PART. Le contexte ne
/// vidait pas les faits de domaine ; l'alerte mourait à la fin du SaveChanges.
/// Ces tests vérifient le chaînon qui manquait, et rien d'autre : ils s'arrêtent
/// à l'Outbox, qui est le point où la publication devient garantie.
/// </remarks>
[Collection(PostgresCollection.Name)]
public sealed class AlerteDeSoldeTests(PostgresFixture fixture)
{
    private static readonly Actor Finance = Actor.Admin("finance-1");

    private static DateTimeOffset Maintenant => new(2026, 9, 29, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Le_franchissement_du_seuil_depose_un_message_dans_l_outbox()
    {
        var compte = Approvisionne("m-alerte", solde: 5_000, seuil: 2_000);

        await using var context = fixture.CreateContext(new BillingIntegrationEventPublisher());
        context.BillingAccounts.Add(compte);
        await context.SaveChangesAsync(CancellationToken.None);

        // 5 000 − 3 500 = 1 500, donc sous le seuil de 2 000.
        context.AccountMovements.Add(
            compte.Debit(MoneyXof.From(3_500), "course-alerte-1", "alerte-1", Finance, Maintenant));
        await context.SaveChangesAsync(CancellationToken.None);

        var messages = await MessagesDuCompte(context, "m-alerte");

        messages.Should().HaveCount(1);
        messages[0].EventType.Should().Be("hba.billing.v1.LowBalanceReached");
        messages[0].PartitionKey.Should().Be("merchant:m-alerte");

        var publie = BillingEvent.Parser.ParseFrom(messages[0].Payload);

        publie.PayloadCase.Should().Be(BillingEvent.PayloadOneofCase.LowBalanceReached);

        var alerte = publie.LowBalanceReached;

        alerte.OwnerType.Should().Be("merchant");
        alerte.OwnerId.Should().Be("m-alerte");
        alerte.Balance.Amount.Should().Be(1_500);
        alerte.Threshold.Amount.Should().Be(2_000);
        alerte.Balance.Currency.Should().Be(MoneyXof.CurrencyCode);
    }

    /// <summary>
    /// UNE FOIS PAR FRANCHISSEMENT, PAS À CHAQUE COURSE. Un titulaire qui reçoit
    /// trente messages par jour n'en lit aucun.
    /// </summary>
    [Fact]
    public async Task Une_seconde_course_sous_le_seuil_ne_realerte_pas()
    {
        var compte = Approvisionne("m-une-fois", solde: 5_000, seuil: 2_000);

        await using var context = fixture.CreateContext(new BillingIntegrationEventPublisher());
        context.BillingAccounts.Add(compte);
        await context.SaveChangesAsync(CancellationToken.None);

        context.AccountMovements.Add(
            compte.Debit(MoneyXof.From(3_500), "course-1", "u-course-1", Finance, Maintenant));
        await context.SaveChangesAsync(CancellationToken.None);

        context.AccountMovements.Add(
            compte.Debit(MoneyXof.From(500), "course-2", "u-course-2", Finance, Maintenant));
        await context.SaveChangesAsync(CancellationToken.None);

        var messages = await MessagesDuCompte(context, "m-une-fois");

        messages.Should().HaveCount(1);
    }

    /// <summary>
    /// LES DÉBITS ET LES CRÉDITS NE SORTENT PAS. Personne ne les consomme ;
    /// publier un flux comptable que rien ne lit obligerait à en tenir le
    /// contrat pour toujours.
    /// </summary>
    [Fact]
    public async Task Un_debit_qui_ne_franchit_rien_ne_publie_rien()
    {
        var compte = Approvisionne("m-silence", solde: 5_000, seuil: 1_000);

        await using var context = fixture.CreateContext(new BillingIntegrationEventPublisher());
        context.BillingAccounts.Add(compte);
        await context.SaveChangesAsync(CancellationToken.None);

        context.AccountMovements.Add(
            compte.Debit(MoneyXof.From(800), "course-1", "s-course-1", Finance, Maintenant));
        await context.SaveChangesAsync(CancellationToken.None);

        var messages = await MessagesDuCompte(context, "m-silence");

        messages.Should().BeEmpty();
    }

    /// <summary>
    /// Les messages d'Outbox DE CE COMPTE, et d'aucun autre.
    /// </summary>
    ///
    /// <remarks>
    /// LA REQUETE PORTAIT SUR TOUT LE TOPIC, ET C'EST CE QUI A FAIT ECHOUER DEUX
    /// DE CES TESTS AU PREMIER PASSAGE REEL.
    ///
    /// Les classes de test partagent une seule base — c'est le principe du
    /// conteneur unique, et c'est voulu : en demarrer un par classe couterait
    /// quinze secondes a chaque fois. Mais alors « tous les messages du topic
    /// billing » contient aussi ceux des autres tests : le premier en trouvait
    /// deux au lieu d'un, et celui qui verifie qu'AUCUN message ne part en
    /// trouvait un, qui n'etait pas le sien.
    ///
    /// LA CLE DE PARTITION EST LE TITULAIRE, donc elle isole exactement ce qu'il
    /// faut, sans rien ajouter au code de production. Chaque test a son propre
    /// identifiant de commercant.
    /// </remarks>
    private static Task<List<OutboxMessage>> MessagesDuCompte(BillingDbContext context, string ownerId)
        => context.OutboxMessages
            .Where(m => m.Topic == KafkaTopics.BillingEvents
                        && m.PartitionKey == $"merchant:{ownerId}")
            .ToListAsync(CancellationToken.None);

    private static BillingAccount Approvisionne(string ownerId, long solde, long seuil)
    {
        var compte = BillingAccount.Open(
            Guid.CreateVersion7(), "merchant", ownerId, MoneyXof.From(seuil), Finance, Maintenant);

        compte.Credit(DomainKind.Topup, MoneyXof.From(solde), "recharge", $"cle-{ownerId}", Finance, Maintenant);

        // LES FAITS DE L'OUVERTURE ET DE LA RECHARGE SONT ECARTES : ils datent
        // d'avant le SaveChanges qu'on observe, et aucun des deux ne franchit le
        // seuil. Les laisser ne changerait rien au compte des messages — le
        // publieur les ignore — mais le test doit dire ce qu'il mesure.
        compte.ClearDomainEvents();

        return compte;
    }
}
