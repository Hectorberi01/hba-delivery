using Hba.Rating.Application.Common;
using Hba.Rating.Domain.Reviews;
using Microsoft.EntityFrameworkCore;

namespace Hba.Rating.Infrastructure.Persistence.Repositories;

internal sealed class ReviewRepository(RatingDbContext context) : IReviewRepository
{
    public void Add(Review review) => context.Reviews.Add(review);

    public Task<Review?> GetAsync(
        MissionType missionType,
        Guid referenceId,
        CancellationToken cancellationToken) =>
        context.Reviews
            .FirstOrDefaultAsync(
                r => r.MissionType == missionType && r.ReferenceId == referenceId,
                cancellationToken);

    public async Task<IReadOnlyList<Review>> FindPendingForCustomerAsync(
        string customerId,
        int limit,
        CancellationToken cancellationToken) =>
        await context.Reviews
            .AsNoTracking()
            .Where(r => r.CustomerId == customerId && r.SubmittedAt == null)
            .OrderByDescending(r => r.MissionCompletedAt)
            .Take(limit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public async Task<IReadOnlyList<Review>> FindSubmittedForDriverAsync(
        Guid driverId,
        int limit,
        CancellationToken cancellationToken) =>
        await context.Reviews
            .AsNoTracking()
            .Where(r => r.DriverId == driverId && r.Stars != null)
            .OrderByDescending(r => r.SubmittedAt)
            .Take(limit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <summary>
    /// LA MOYENNE SE CALCULE EN BASE, EN UN ALLER-RETOUR.
    /// </summary>
    ///
    /// <remarks>
    /// AUCUN AVIS REND (0, 0), PAS UNE EXCEPTION. « Average » sur une sequence
    /// vide leve en SQL comme en memoire ; le cas est normal — tout livreur
    /// commence la — et c'est a la vue de decider qu'on n'affiche rien.
    ///
    /// LES NON NOTEES SONT ECARTEES. Les compter comme des zeros ferait tomber la
    /// moyenne de chacun a mesure que les clients ne notent pas, ce qui punirait
    /// les livreurs pour le silence de leurs passagers.
    /// </remarks>
    public async Task<(double Moyenne, int Nombre)> ResumerPourDriverAsync(
        Guid driverId,
        CancellationToken cancellationToken)
    {
        var resume = await context.Reviews
            .AsNoTracking()
            .Where(r => r.DriverId == driverId && r.Stars != null)
            .GroupBy(_ => 1)
            .Select(g => new { Somme = g.Sum(r => r.Stars!.Value), Nombre = g.Count() })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return resume is null || resume.Nombre == 0
            ? (0, 0)
            : ((double)resume.Somme / resume.Nombre, resume.Nombre);
    }
}
