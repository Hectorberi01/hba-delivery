using Hba.Payment.Application.Common.Interfaces;
using Hba.Payment.Domain.Earnings;
using Microsoft.EntityFrameworkCore;

namespace Hba.Payment.Infrastructure.Persistence.Reads;

/// <summary>
/// Le compte d'un livreur, en deux lectures.
///
/// LES TOTAUX ET LES LIGNES NE SE LISENT PAS ENSEMBLE, ET C'EST LE POINT.
/// Les totaux portent sur TOUT le compte ; les lignes sont plafonnees pour
/// que l'ecran reste leger. Les calculer a partir des lignes rendues
/// donnerait un « reste du » qui diminue quand on demande moins de lignes —
/// exactement le defaut que ce relevé remplace.
/// </summary>
internal sealed class DriverStatementReader(PaymentDbContext context) : IDriverStatementReader
{
    public async Task<DriverStatementView> ReadAsync(
        string driverId,
        int limit,
        CancellationToken cancellationToken)
    {
        var compte = context.DriverLedgerEntries
            .AsNoTracking()
            .Where(e => e.DriverId == driverId);

        // UNE SEULE REQUETE POUR LES DEUX SENS. Deux agregats separes
        // feraient deux allers-retours pour lire la meme table, et surtout
        // pourraient tomber de part et d'autre d'une ecriture concurrente :
        // le gagne d'avant et le verse d'apres, donc un reste du faux.
        var cumuls = await compte
            .GroupBy(e => e.Direction)
            .Select(g => new { Direction = g.Key, Total = g.Sum(e => e.AmountXof), Lignes = g.LongCount() })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var gagne = cumuls.Where(c => c.Direction == LedgerDirection.Credit).Sum(c => c.Total);
        var verse = cumuls.Where(c => c.Direction == LedgerDirection.Debit).Sum(c => c.Total);
        var total = cumuls.Sum(c => c.Lignes);

        var lignes = await compte
            .OrderByDescending(e => e.OccurredAt)
            .ThenByDescending(e => e.Id)
            .Take(limit)
            .Select(e => new LedgerEntryView(
                e.Id,
                e.Kind,
                e.Direction,
                e.AmountXof,
                e.DeliveryId,
                e.DeliveryReference,
                e.PayoutId,
                e.OccurredAt))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // LE RESTE DU PEUT ETRE NEGATIF, ET ON NE LE CACHE PAS. Cela
        // signifierait qu'on a verse plus que du — une erreur de saisie de la
        // finance, par exemple. La ramener a zero ferait disparaitre le seul
        // signal qui permet de la voir.
        return new DriverStatementView(driverId, gagne, verse, gagne - verse, lignes, total);
    }
}
