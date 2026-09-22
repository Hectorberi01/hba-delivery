using Hba.Directory.Domain.Customers;
using Hba.Directory.Domain.Merchants;

namespace Hba.Directory.Application.Ports;

public interface ICustomerRepository
{
    Task<Customer?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken);

    void Add(Customer customer);
}

public interface IMerchantRepository
{
    Task<Merchant?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Recherche le commerçant propriétaire d'un point de collecte.</summary>
    Task<Merchant?> GetByPickupPointIdAsync(Guid pickupPointId, CancellationToken cancellationToken);

    Task<IReadOnlyList<Merchant>> SearchAsync(
        string? query,
        bool onlyActive,
        int pageSize,
        int offset,
        CancellationToken cancellationToken);

    Task<int> CountAsync(string? query, bool onlyActive, CancellationToken cancellationToken);

    void Add(Merchant merchant);
}
