using Hba.Payment.Application.Common.Interfaces;
using Hba.Payment.Domain.Earnings;
using Hba.Payment.Domain.Payments;
using Microsoft.EntityFrameworkCore;

namespace Hba.Payment.Infrastructure.Persistence.Repositories;

internal sealed class DriverLedgerRepository(PaymentDbContext context) : IDriverLedgerRepository
{
    public Task<DriverLedgerEntry?> FindMissionEntryAsync(
        LedgerEntryKind kind,
        ServiceType serviceType,
        Guid referenceId,
        CancellationToken cancellationToken)
        => context.DriverLedgerEntries.FirstOrDefaultAsync(
            e => e.ServiceType == serviceType
                 && e.ReferenceId == referenceId
                 && e.Kind == kind,
            cancellationToken);

    public async Task<long> DueForPayoutAsync(
        string driverId,
        DateTimeOffset creditsBefore,
        CancellationToken cancellationToken)
    {
        var compte = context.DriverLedgerEntries.AsNoTracking().Where(e => e.DriverId == driverId);

        // DEUX SOMMES, UNE SEULE REQUETE. Les separer les ferait tomber de
        // part et d'autre d'une ecriture concurrente : les credits d'avant et
        // les debits d'apres, donc un disponible trop eleve — et c'est
        // exactement le chiffre sur lequel on autorise un retrait.
        var cumuls = await compte
            .GroupBy(e => 1)
            .Select(g => new
            {
                Credits = g.Sum(e => e.Direction == LedgerDirection.Credit && e.OccurredAt <= creditsBefore
                    ? e.AmountXof
                    : 0L),
                Debits = g.Sum(e => e.Direction == LedgerDirection.Debit ? e.AmountXof : 0L),
            })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return cumuls is null ? 0 : cumuls.Credits - cumuls.Debits;
    }

    public void Add(DriverLedgerEntry entry) => context.DriverLedgerEntries.Add(entry);
}
