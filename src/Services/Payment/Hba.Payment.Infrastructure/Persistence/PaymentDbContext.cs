using Hba.BuildingBlocks.Application.Abstractions;
using Hba.BuildingBlocks.Domain;
using Hba.BuildingBlocks.Messaging.Inbox;
using Hba.BuildingBlocks.Messaging.Outbox;
using Hba.BuildingBlocks.Persistence;
using Hba.BuildingBlocks.Persistence.Configurations;
using Hba.Payment.Application.Common.IntegrationEvents;
using Hba.Payment.Domain.Earnings;
using Hba.Payment.Domain.Payments;
using Hba.Payment.Domain.Payouts;
using Microsoft.EntityFrameworkCore;

namespace Hba.Payment.Infrastructure.Persistence;

/// <summary>
/// Base du service Payment. Une base par service : aucun autre service n'y
/// accede, meme en lecture. C'est ici que vivent les references du fournisseur,
/// et nulle part ailleurs.
/// </summary>
public sealed class PaymentDbContext(
    DbContextOptions<PaymentDbContext> options,
    IPaymentIntegrationEventPublisher integrationEvents) : DbContext(options), IUnitOfWork
{
    public const string Schema = "payment";

    public static readonly EfMessagingOptions MessagingTables = new() { Schema = Schema };

    public DbSet<PaymentIntent> PaymentIntents => Set<PaymentIntent>();

    /// <summary>
    /// Le grand livre des livreurs.
    ///
    /// IL NE PASSE PAS PAR DrainDomainEvents, ET C'EST NORMAL : une ligne de
    /// compte n'annonce rien a personne. Elle constate, et c'est le releve qui
    /// la lit.
    /// </summary>
    public DbSet<DriverLedgerEntry> DriverLedgerEntries => Set<DriverLedgerEntry>();

    /// <summary>
    /// Les demandes de versement. Elles ne passent pas non plus par
    /// DrainDomainEvents : aucun autre service n'a affaire a ce qu'un livreur
    /// demande, tant que rien n'est verse.
    /// </summary>
    public DbSet<PayoutRequest> PayoutRequests => Set<PayoutRequest>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();

    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(PaymentDbContext).Assembly);
        modelBuilder.ApplyHbaMessagingModel(MessagingTables);
    }

    /// <summary>
    /// Les faits de domaine deviennent des messages d'Outbox JUSTE AVANT le
    /// commit : ils partagent la transaction du changement metier. Un paiement
    /// encaisse et un « PaymentSucceeded » non publie ne peuvent donc pas
    /// coexister.
    /// </summary>
    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        DrainDomainEvents();
        return await base.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private void DrainDomainEvents()
    {
        var outbox = new EfOutbox(this);

        var aggregates = ChangeTracker
            .Entries<PaymentIntent>()
            .Select(entry => entry.Entity)
            .Where(aggregate => aggregate.DomainEvents.Count > 0)
            .ToList();

        foreach (var aggregate in aggregates)
        {
            IDomainEvent[] events = [.. aggregate.DomainEvents];
            aggregate.ClearDomainEvents();

            foreach (var domainEvent in events)
            {
                integrationEvents.Publish(outbox, aggregate, domainEvent);
            }
        }
    }
}
