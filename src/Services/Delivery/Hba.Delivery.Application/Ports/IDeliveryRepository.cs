using Hba.Delivery.Domain.Deliveries;

namespace Hba.Delivery.Application.Ports;

public interface IDeliveryRepository
{
    Task<DeliveryAggregate?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Recherche par identifiant de commande externe, dans le périmètre d'un
    /// partenaire. Sert à l'idempotence des créations B2B.
    /// </summary>
    Task<DeliveryAggregate?> GetByExternalOrderIdAsync(
        string partnerId,
        string externalOrderId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<DeliveryAggregate>> ListAsync(
        DeliveryQueryFilter filter,
        CancellationToken cancellationToken);

    void Add(DeliveryAggregate delivery);
}

/// <summary>
/// Filtre de lecture. Le périmètre (client, commerçant, partenaire) n'est jamais
/// laissé à l'appelant : il est imposé par la couche Application.
/// </summary>
public sealed record DeliveryQueryFilter
{
    public string? CustomerId { get; init; }

    public string? MerchantId { get; init; }

    public string? PartnerId { get; init; }

    public string? DriverId { get; init; }

    public IReadOnlyCollection<DeliveryStatus>? Statuses { get; init; }

    public DateTimeOffset? CreatedAfter { get; init; }

    public DateTimeOffset? CreatedBefore { get; init; }

    public int PageSize { get; init; } = 25;

    public int Offset { get; init; }
}
