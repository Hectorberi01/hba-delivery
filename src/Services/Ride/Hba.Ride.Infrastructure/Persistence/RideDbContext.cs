using Hba.BuildingBlocks.Application.Abstractions;
using Hba.BuildingBlocks.Domain;
using Hba.BuildingBlocks.Messaging.Inbox;
using Hba.BuildingBlocks.Messaging.Outbox;
using Hba.BuildingBlocks.Persistence;
using Hba.BuildingBlocks.Persistence.Configurations;
using Hba.Ride.Application.Common.IntegrationEvents;
using Hba.Ride.Domain.Rides;
using Microsoft.EntityFrameworkCore;

namespace Hba.Ride.Infrastructure.Persistence;

/// <summary>
/// Base du service Ride : les courses de personnes, rien d'autre.
/// </summary>
///
/// <remarks>
/// UNE BASE PAR SERVICE : aucun autre service n'y accède, même en lecture. Les
/// courses de colis vivent dans la base de Delivery, et les deux ne se joignent
/// jamais en SQL — c'est précisément ce que l'événementiel remplace.
///
/// L'ÉCRITURE CONCURRENTE N'EST PAS TRADUITE ICI, CONTRAIREMENT À DRIVER. Le
/// jeton xmin est bien posé, donc deux acceptations concurrentes ne gagnent pas
/// toutes les deux ; mais <c>DbUpdateConcurrencyException</c> ressort telle
/// quelle, en 500. Driver la traduit en refus métier et la plupart des autres
/// services ne le font pas non plus. Le faire proprement ici demanderait un code
/// de refus de plus et une décision sur ce que l'application cliente doit
/// réessayer : à traiter pour tous les services d'un coup, pas service par
/// service au hasard de l'écriture.
/// </remarks>
public sealed class RideDbContext(
    DbContextOptions<RideDbContext> options,
    IRideIntegrationEventPublisher integrationEvents) : DbContext(options), IUnitOfWork
{
    public const string Schema = "ride";

    public static readonly EfMessagingOptions MessagingTables = new() { Schema = Schema };

    public DbSet<RideAggregate> Rides => Set<RideAggregate>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();

    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(RideDbContext).Assembly);
        modelBuilder.ApplyHbaMessagingModel(MessagingTables);
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        DrainDomainEvents();

        return await base.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Les faits du domaine deviennent des messages Outbox DANS LA MÊME
    /// TRANSACTION que l'écriture de la course (ADR 0003).
    /// </summary>
    ///
    /// <remarks>
    /// L'ORDRE EST CE QUI REND LE PROCÉDÉ SÛR : vider la liste AVANT d'écrire,
    /// et dans la même transaction. Publier après le commit laisserait perdre un
    /// message si le processus tombe entre les deux ; publier sans vider le
    /// republierait au prochain enregistrement.
    /// </remarks>
    private void DrainDomainEvents()
    {
        var outbox = new EfOutbox(this);

        var courses = ChangeTracker
            .Entries<RideAggregate>()
            .Select(entry => entry.Entity)
            .Where(course => course.DomainEvents.Count > 0)
            .ToList();

        foreach (var course in courses)
        {
            IDomainEvent[] faits = [.. course.DomainEvents];
            course.ClearDomainEvents();

            foreach (var fait in faits)
            {
                integrationEvents.Publish(outbox, course, fait);
            }
        }
    }
}
