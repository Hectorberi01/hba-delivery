using Hba.Driver.Application.Common.Interfaces;
using Hba.Driver.Domain.Drivers;
using Microsoft.EntityFrameworkCore;

namespace Hba.Driver.Infrastructure.Persistence.Repositories;

internal sealed class DriverRepository(DriverDbContext context) : IDriverRepository
{
    public Task<DriverAggregate?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
        => context.Drivers.FirstOrDefaultAsync(d => d.Id == id, cancellationToken);

    /// <summary>
    /// Les livreurs dont on a l'identifiant, sans aucun filtre d'etat.
    ///
    /// PAS FindAvailableAsync : celui-ci ne rend que les livreurs verifies ET
    /// disponibles, ce qui convient au dispatch et fausse une carte. Un
    /// livreur en course est exactement celui qu'ops veut voir bouger.
    /// </summary>
    public async Task<IReadOnlyList<DriverAggregate>> FindByIdsAsync(
        IReadOnlyCollection<Guid> ids,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ids);

        if (ids.Count == 0)
        {
            return [];
        }

        return await context.Drivers
            .AsNoTracking()
            .Where(d => ids.Contains(d.Id))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<DriverAggregate>> FindAvailableAsync(
        IReadOnlyCollection<Guid> ids,
        VehicleType? vehicleType,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ids);

        if (ids.Count == 0)
        {
            return [];
        }

        var query = context.Drivers
            .AsNoTracking()
            .Where(d => ids.Contains(d.Id)
                        && d.VerificationStatus == VerificationStatus.Verified
                        && d.OperationalStatus == OperationalStatus.Available);

        // UN TYPE NON PRECISE VEUT DIRE « N'IMPORTE LEQUEL », pas « le type
        // zero ». Le contrat expose VEHICLE_TYPE_UNSPECIFIED comme valeur par
        // defaut de protobuf : la traiter comme un filtre ne rendrait jamais
        // personne.
        if (vehicleType is not null && vehicleType != VehicleType.Unspecified)
        {
            query = query.Where(d => d.Vehicle.Type == vehicleType);
        }

        return await query.ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<(IReadOnlyList<DriverAggregate> Drivers, int Total)> ListAsync(
        string? query,
        VerificationStatus? verificationStatus,
        OperationalStatus? operationalStatus,
        int pageSize,
        int offset,
        CancellationToken cancellationToken)
    {
        var filtree = context.Drivers.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(query))
        {
            var terme = query.Trim();

            // ILIKE plutot que ToLower() des deux cotes : Npgsql traduit
            // EF.Functions.ILike en comparaison insensible a la casse cote
            // base, sans empecher l'usage d'un index trigramme le jour ou
            // l'annuaire grandira.
            filtree = filtree.Where(d =>
                EF.Functions.ILike(d.DisplayName, $"%{terme}%")
                || EF.Functions.ILike(d.Phone, $"%{terme}%"));
        }

        // UNSPECIFIED VEUT DIRE « TOUS ». C'est la valeur par defaut de
        // protobuf : la traiter comme un filtre ne rendrait jamais personne.
        if (verificationStatus is not null && verificationStatus != VerificationStatus.Unspecified)
        {
            filtree = filtree.Where(d => d.VerificationStatus == verificationStatus);
        }

        if (operationalStatus is not null && operationalStatus != OperationalStatus.Unspecified)
        {
            filtree = filtree.Where(d => d.OperationalStatus == operationalStatus);
        }

        var total = await filtree.CountAsync(cancellationToken).ConfigureAwait(false);

        var page = await filtree
            .OrderByDescending(d => d.RegisteredAt)
            .ThenBy(d => d.Id)
            .Skip(Math.Max(offset, 0))
            .Take(Math.Clamp(pageSize, 1, 200))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return (page, total);
    }

    public void Add(DriverAggregate driver) => context.Drivers.Add(driver);
}
