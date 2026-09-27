using Hba.BuildingBlocks.Application.Abstractions;
using Hba.BuildingBlocks.Domain;
using Hba.BuildingBlocks.Messaging.Inbox;
using Hba.BuildingBlocks.Messaging.Outbox;
using Hba.BuildingBlocks.Persistence;
using Hba.BuildingBlocks.Persistence.Configurations;
using Hba.Driver.Application.Common.IntegrationEvents;
using Hba.Driver.Domain.Drivers;
using Hba.Driver.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Logging;

namespace Hba.Driver.Infrastructure.Persistence;

/// <summary>
/// Base du service Driver : les dossiers et les etats, jamais les positions.
/// Une base par service : aucun autre service n'y accede, meme en lecture.
/// </summary>
public sealed class DriverDbContext(
    DbContextOptions<DriverDbContext> options,
    IDriverIntegrationEventPublisher integrationEvents,
    ILogger<DriverDbContext> journal) : DbContext(options), IUnitOfWork
{
    public const string Schema = "driver";

    public static readonly EfMessagingOptions MessagingTables = new() { Schema = Schema };

    public DbSet<DriverAggregate> Drivers => Set<DriverAggregate>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();

    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(DriverDbContext).Assembly);
        modelBuilder.ApplyHbaMessagingModel(MessagingTables);
    }

    /// <summary>
    /// UNE COURSE PERDUE N'EST PAS UNE PANNE. Le jeton xmin existe pour que
    /// deux ecritures concurrentes sur le meme dossier ne gagnent pas toutes
    /// les deux : quand l'une perd, EF leve DbUpdateConcurrencyException.
    /// Rien ne la rattrapait, donc elle ressortait en 500 « erreur interne »
    /// avec une reference a recopier — pour un cas ou la bonne reponse est
    /// « quelqu'un a modifie ce dossier, relis et recommence ». On la traduit
    /// ici, au seul endroit qui connait a la fois EF et le vocabulaire du
    /// domaine ; l'intercepteur gRPC et le filtre HTTP en font un 409.
    ///
    /// LES AUTRES SERVICES ONT LE MEME TROU. Identity, Delivery, Dispatch et
    /// les autres portent le meme jeton xmin sans cette traduction. Ce n'est
    /// pas corrige ici : leurs DbContext sont a modifier un par un, et le
    /// faire sans pouvoir compiler ni tester chacun serait pire que le trou.
    /// </summary>
    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        DrainDomainEvents();

        try
        {
            return await base.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            // CE QUI A PERDU, NOMME. Sans cette ligne, la traduction en
            // DomainException efface la seule information utile : le message
            // d'EF ne dit jamais QUELLE instruction du lot a compte zero
            // ligne, alors que l'exception, elle, porte les entrees en
            // echec. Les journaliser coute une ligne et evite de deviner.
            foreach (var entree in exception.Entries)
            {
                journal.LogError(
                    "Ecriture concurrente perdue : {Entite} en etat {Etat}. Cle : {Cle}. Jeton lu : {Jeton}.",
                    entree.Metadata.DisplayName(),
                    entree.State,
                    Decrire(entree, cle: true),
                    Decrire(entree, cle: false));
            }

            throw new DomainException(
                DriverErrorCodes.ConcurrentModification,
                "Ce dossier a ete modifie entre-temps. Rechargez-le et recommencez.",
                exception);
        }
    }

    /// <summary>
    /// Cle primaire (<paramref name="cle"/> vrai) ou jetons de concurrence de
    /// l'entree, sous forme lisible dans un journal.
    /// </summary>
    private static string Decrire(EntityEntry entree, bool cle)
    {
        IEnumerable<IProperty> proprietes;

        if (cle)
        {
            var primaire = entree.Metadata.FindPrimaryKey();
            proprietes = primaire is null ? Enumerable.Empty<IProperty>() : primaire.Properties;
        }
        else
        {
            proprietes = entree.Metadata.GetProperties().Where(p => p.IsConcurrencyToken);
        }

        var morceaux = proprietes.Select(p => cle
            ? $"{p.Name}={entree.Property(p.Name).CurrentValue}"
            : $"{p.Name}={entree.Property(p.Name).OriginalValue}");

        return string.Join(", ", morceaux);
    }

    private void DrainDomainEvents()
    {
        var outbox = new EfOutbox(this);

        var aggregates = ChangeTracker
            .Entries<DriverAggregate>()
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
