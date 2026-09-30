using Hba.BuildingBlocks.Application.Abstractions;
using Hba.BuildingBlocks.Domain;
using Hba.BuildingBlocks.Messaging.Inbox;
using Hba.BuildingBlocks.Messaging.Outbox;
using Hba.BuildingBlocks.Persistence;
using Hba.BuildingBlocks.Persistence.Configurations;
using Hba.Directory.Application.IntegrationEvents;
using Hba.Directory.Domain.Customers;
using Hba.Directory.Domain.Merchants;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Logging;

namespace Hba.Directory.Infrastructure.Persistence;

public sealed class DirectoryDbContext(
    DbContextOptions<DirectoryDbContext> options,
    IDirectoryIntegrationEventPublisher integrationEvents,
    ILogger<DirectoryDbContext> journal) : DbContext(options), IUnitOfWork
{
    public const string Schema = "directory";

    public static readonly EfMessagingOptions MessagingTables = new() { Schema = Schema };

    public DbSet<Customer> Customers => Set<Customer>();

    public DbSet<Merchant> Merchants => Set<Merchant>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();

    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(DirectoryDbContext).Assembly);
        modelBuilder.ApplyHbaMessagingModel(MessagingTables);
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        // L'Outbox est construite sur CE contexte : l'injecter dans le
        // publieur formerait un cycle, puisque EfOutbox écrit dans le
        // DbContext qui a besoin du publieur.
        var outbox = new EfOutbox(this);

        Drain<Customer>((aggregate, domainEvent) => integrationEvents.Publish(outbox, aggregate, domainEvent));
        Drain<Merchant>((aggregate, domainEvent) => integrationEvents.Publish(outbox, aggregate, domainEvent));

        // CE QUE LE SUIVI CROIT, JUSTE AVANT D'ECRIRE.
        //
        // C'est la seule chose que le journal ne disait pas. « 0 lignes
        // affectees » designe le symptome ; l'etat de chaque entite — Added,
        // Modified, Unchanged — designe la cause. Une adresse neuve en
        // « Modified » et un UPDATE s'expliquent l'un l'autre ; en « Added »
        // avec un UPDATE quand meme, c'est ailleurs qu'il faut chercher.
        //
        // DebugView.LongView DESERIALISE TOUT LE GRAPHE : il est cher, et il
        // affiche les valeurs. Il ne se calcule que si la trace est demandee,
        // et EnableSensitiveDataLogging refuse deja de s'activer hors
        // developpement.
        if (this.GetService<IDbContextOptions>()
                .FindExtension<CoreOptionsExtension>()?.IsSensitiveDataLoggingEnabled == true)
        {
            var etats = ChangeTracker.Entries()
                .Where(e => e.State != EntityState.Unchanged)
                .Select(e => $"{e.Metadata.ShortName()} = {e.State}")
                .ToList();

            journal.LogWarning(
                "ChangeTracker avant enregistrement : {Etats}.{NouvelleLigne}{Detail}",
                etats.Count == 0 ? "(rien a ecrire)" : string.Join(", ", etats),
                Environment.NewLine,
                ChangeTracker.DebugView.LongView);
        }

        return await base.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private void Drain<TAggregate>(Action<TAggregate, IDomainEvent> publish)
        where TAggregate : AggregateRoot
    {
        var aggregates = ChangeTracker
            .Entries<TAggregate>()
            .Select(entry => entry.Entity)
            .Where(aggregate => aggregate.DomainEvents.Count > 0)
            .ToList();

        foreach (var aggregate in aggregates)
        {
            IDomainEvent[] events = [.. aggregate.DomainEvents];
            aggregate.ClearDomainEvents();

            foreach (var domainEvent in events)
            {
                publish(aggregate, domainEvent);
            }
        }
    }
}
