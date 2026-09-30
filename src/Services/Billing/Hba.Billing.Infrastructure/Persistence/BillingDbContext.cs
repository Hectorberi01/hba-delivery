using Hba.BuildingBlocks.Application.Abstractions;
using Hba.BuildingBlocks.Messaging.Inbox;
using Hba.BuildingBlocks.Messaging.Outbox;
using Hba.BuildingBlocks.Persistence;
using Hba.BuildingBlocks.Persistence.Configurations;
using Hba.Billing.Application.IntegrationEvents;
using Hba.Billing.Domain.Accounts;
using Hba.BuildingBlocks.Domain;
using Microsoft.EntityFrameworkCore;

namespace Hba.Billing.Infrastructure.Persistence;

/// <summary>
/// Base du service Billing. Une base par service : aucun autre n'y accède,
/// même en lecture.
/// </summary>
///
/// <remarks>
/// UN SEUL FAIT SORT DE CE SERVICE : « LowBalanceReached ». Le titulaire d'un
/// compte prépayé qui passe sous son seuil doit être prévenu, sinon il découvre
/// son solde vide au moment où une course est refusée.
///
/// LES DÉBITS ET LES CRÉDITS NE SORTENT PAS, et le publieur explique pourquoi :
/// personne ne les consomme. Le drainage ci-dessous les voit et les jette après
/// les avoir proposés — c'est le publieur qui filtre, pas ce contexte.
///
/// AU SAVECHANGES, DONC DANS LA TRANSACTION DU CHANGEMENT MÉTIER. Un solde
/// descendu sous le seuil et une alerte non publiée ne peuvent pas coexister :
/// ils sont écrits ensemble ou pas du tout.
/// </remarks>
public sealed class BillingDbContext(
    DbContextOptions<BillingDbContext> options,
    IBillingIntegrationEventPublisher integrationEvents) : DbContext(options), IUnitOfWork
{
    public const string Schema = "billing";

    public static readonly EfMessagingOptions MessagingTables = new() { Schema = Schema };

    public DbSet<BillingAccount> BillingAccounts => Set<BillingAccount>();

    public DbSet<AccountMovement> AccountMovements => Set<AccountMovement>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();

    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(BillingDbContext).Assembly);
        modelBuilder.ApplyHbaMessagingModel(MessagingTables);
    }

    /// <summary>
    /// Les faits de domaine deviennent des messages d'Outbox JUSTE AVANT le
    /// commit : ils partagent la transaction du changement métier.
    /// </summary>
    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        DrainDomainEvents();
        return await base.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// LES MOUVEMENTS N'EN PASSENT PAS PAR ICI, ET CE N'EST PAS UN OUBLI : une
    /// ligne comptable n'annonce rien à personne. Tous les faits de ce service
    /// sont levés par l'agrégat, qui est le seul à connaître le solde.
    /// </summary>
    private void DrainDomainEvents()
    {
        var outbox = new EfOutbox(this);

        var comptes = ChangeTracker
            .Entries<BillingAccount>()
            .Select(entry => entry.Entity)
            .Where(compte => compte.DomainEvents.Count > 0)
            .ToList();

        foreach (var compte in comptes)
        {
            IDomainEvent[] faits = [.. compte.DomainEvents];
            compte.ClearDomainEvents();

            foreach (var fait in faits)
            {
                integrationEvents.Publish(outbox, compte, fait);
            }
        }
    }
}
