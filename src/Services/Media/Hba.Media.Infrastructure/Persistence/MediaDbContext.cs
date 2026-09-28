using Hba.BuildingBlocks.Application.Abstractions;
using Hba.Media.Domain.Assets;
using Microsoft.EntityFrameworkCore;

namespace Hba.Media.Infrastructure.Persistence;

/// <summary>
/// AUCUNE OUTBOX, AUCUNE INBOX, ET C'EST DELIBERE. Media ne publie rien et ne
/// consomme rien : un fichier deposé n'est un evenement pour personne, et ce
/// sont les services proprietaires qui annoncent ce qu'ils en font. Ajouter les
/// tables de messagerie « au cas ou » creerait deux publicateurs a surveiller
/// pour zero message.
/// </summary>
public sealed class MediaDbContext(DbContextOptions<MediaDbContext> options)
    : DbContext(options), IUnitOfWork
{
    public const string Schema = "media";

    public DbSet<MediaAsset> Assets => Set<MediaAsset>();

    public DbSet<MediaAccessRecord> AccessRecords => Set<MediaAccessRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(MediaDbContext).Assembly);
    }
}
