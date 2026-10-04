using Hba.Ride.Application.Ports;
using Hba.Ride.Domain.Rides;
using Hba.Ride.Infrastructure.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;

namespace Hba.Ride.Infrastructure.Persistence.Repositories;

internal sealed class RideRepository(RideDbContext context) : IRideRepository
{
    public Task<RideAggregate?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
        => context.Rides.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

    /// <summary>
    /// Les courses ouvertes d'un client, la plus récente d'abord.
    /// </summary>
    ///
    /// <remarks>
    /// LE FILTRE EST CELUI DE L'INDEX PARTIEL, mot pour mot — il vient de
    /// <see cref="RideConfiguration.EtatsEnCours"/>. Deux listes d'états
    /// finiraient par diverger, et l'index cesserait de couvrir la requête sans
    /// que rien ne le signale.
    ///
    /// SANS SUIVI : c'est une lecture d'affichage. Les suivre chargerait le
    /// ChangeTracker de courses que personne ne va modifier, et un
    /// <c>SaveChangesAsync</c> ultérieur les réécrirait pour rien.
    /// </remarks>
    public async Task<IReadOnlyList<RideAggregate>> ListActiveForCustomerAsync(
        string customerId,
        CancellationToken cancellationToken)
        => await context.Rides
            .AsNoTracking()
            .Where(r => r.CustomerId == customerId && RideConfiguration.EtatsEnCours.Contains(r.Status))
            .OrderByDescending(r => r.RequestedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <summary>
    /// La course ouverte d'un chauffeur. Suivie, elle : le chauffeur enchaîne
    /// aussitôt une transition dessus.
    /// </summary>
    public Task<RideAggregate?> FindActiveForDriverAsync(string driverId, CancellationToken cancellationToken)
        => context.Rides
            .Where(r => r.DriverId == driverId && RideConfiguration.EtatsEnCours.Contains(r.Status))
            .OrderByDescending(r => r.RequestedAt)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<(IReadOnlyList<RideAggregate> Rides, int Total)> ListForCustomerAsync(
        string customerId,
        int pageSize,
        int offset,
        CancellationToken cancellationToken)
    {
        var requete = context.Rides.AsNoTracking().Where(r => r.CustomerId == customerId);

        // LE TOTAL EST COMPTÉ AVANT LA PAGE, sur la même requête : l'application
        // affiche « 3 sur 47 », et un total déduit de la page rendue dirait
        // toujours la taille de la page.
        var total = await requete.CountAsync(cancellationToken).ConfigureAwait(false);

        var courses = await requete
            .OrderByDescending(r => r.RequestedAt)
            .Skip(offset)
            .Take(pageSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return (courses, total);
    }

    public async Task<IReadOnlyList<RideAggregate>> ListUnpaidBeforeAsync(
        DateTimeOffset limite,
        int batchSize,
        CancellationToken cancellationToken)
    {
        // « PaymentIntentId != null » EST LA MEME GARDE QUE DU COTE DES COLIS : une
        // course dont l'ouverture du paiement a echoue n'a pas d'intention, et la
        // balayer sur le seul statut emporterait celles que le fournisseur vient de
        // refuser, dans la seconde qui separe la creation du rattachement.
        //
        // LES PLUS ANCIENNES D'ABORD, pour que le balayage avance au lieu de
        // repasser sur les memes lignes.
        return await context.Rides
            .Where(r => r.Status == RideStatus.Requested
                && r.PaymentIntentId != null
                && r.RequestedAt <= limite)
            .OrderBy(r => r.RequestedAt)
            .Take(Math.Clamp(batchSize, 1, 500))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public void Add(RideAggregate ride) => context.Rides.Add(ride);
}
