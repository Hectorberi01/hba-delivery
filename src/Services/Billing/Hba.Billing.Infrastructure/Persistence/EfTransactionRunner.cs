using Hba.Billing.Application.Common.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Hba.Billing.Infrastructure.Persistence;

/// <summary>
/// Le port <see cref="ITransactionRunner"/>, sur la transaction d'EF Core.
/// </summary>
///
/// <remarks>
/// PAS DE STRATÉGIE DE REPRISE À TRAVERSER, ET C'EST À VÉRIFIER SI ÇA CHANGE. Une
/// transaction ouverte à la main est incompatible avec un
/// <c>EnableRetryOnFailure</c> : il faudrait passer par
/// <c>Database.CreateExecutionStrategy().ExecuteAsync(...)</c>, sinon EF lève au
/// premier appel. Aucun service de ce dépôt n'active la reprise aujourd'hui —
/// vérifié le 30 septembre 2026 — et le jour où Billing l'activera, c'est ici
/// qu'il faudra regarder.
/// </remarks>
internal sealed class EfTransactionRunner(BillingDbContext context) : ITransactionRunner
{
    public async Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> travail,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(travail);

        // DÉJÀ DANS UNE TRANSACTION : on n'en ouvre pas une seconde. PostgreSQL
        // ne les imbrique pas, et ouvrir un « BEGIN » par-dessus un autre ferait
        // perdre tout ce qui a été verrouillé avant. L'appelant extérieur reste
        // celui qui valide.
        if (context.Database.CurrentTransaction is not null)
        {
            return await travail(cancellationToken).ConfigureAwait(false);
        }

        await using var transaction = await context.Database
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        var resultat = await travail(cancellationToken).ConfigureAwait(false);

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        // PAS DE ROLLBACK EXPLICITE : le « await using » s'en charge quand une
        // exception traverse. L'écrire dans un catch obligerait à relancer, et
        // une faute relancée depuis un catch perd sa pile d'origine.
        return resultat;
    }
}
