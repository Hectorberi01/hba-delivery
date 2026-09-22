using Hba.Directory.Application.Ports;
using Hba.Directory.Domain.Customers;
using Hba.Directory.Domain.Merchants;
using Microsoft.EntityFrameworkCore;

namespace Hba.Directory.Infrastructure.Persistence;

internal sealed class CustomerRepository(DirectoryDbContext context) : ICustomerRepository
{
    public Task<Customer?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
        => context.Customers.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

    public Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken)
        => context.Customers.AnyAsync(c => c.Id == id, cancellationToken);

    public void Add(Customer customer) => context.Customers.Add(customer);
}

internal sealed class MerchantRepository(DirectoryDbContext context) : IMerchantRepository
{
    public Task<Merchant?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
        => context.Merchants.FirstOrDefaultAsync(m => m.Id == id, cancellationToken);

    /// <summary>
    /// Remonte au commerçant depuis un de ses points de collecte. La requête
    /// passe par la collection possédée : pas de table à interroger à part.
    /// </summary>
    public Task<Merchant?> GetByPickupPointIdAsync(Guid pickupPointId, CancellationToken cancellationToken)
        => context.Merchants
            .FirstOrDefaultAsync(m => m.PickupPoints.Any(p => p.Id == pickupPointId), cancellationToken);

    public async Task<IReadOnlyList<Merchant>> SearchAsync(
        string? query,
        bool onlyActive,
        int pageSize,
        int offset,
        CancellationToken cancellationToken)
        => await Filter(query, onlyActive)
            .OrderBy(m => m.LegalName)
            .Skip(offset)
            .Take(pageSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public Task<int> CountAsync(string? query, bool onlyActive, CancellationToken cancellationToken)
        => Filter(query, onlyActive).CountAsync(cancellationToken);

    public void Add(Merchant merchant) => context.Merchants.Add(merchant);

    private IQueryable<Merchant> Filter(string? query, bool onlyActive)
    {
        var merchants = context.Merchants.AsQueryable();

        if (onlyActive)
        {
            merchants = merchants.Where(m => m.IsActive);
        }

        if (!string.IsNullOrWhiteSpace(query))
        {
            var pattern = $"%{query.Trim()}%";

            // ILIKE : la recherche ne doit pas dépendre de la casse ni des
            // accents saisis par l'opérateur.
            merchants = merchants.Where(m =>
                EF.Functions.ILike(m.LegalName, pattern)
                || EF.Functions.ILike(m.ContactName, pattern)
                || EF.Functions.ILike(m.ContactPhone, pattern));
        }

        return merchants;
    }

}
