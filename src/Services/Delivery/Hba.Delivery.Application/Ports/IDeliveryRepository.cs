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

    /// <summary>
    /// Les courses qui attendent un paiement depuis trop longtemps.
    /// </summary>
    ///
    /// <remarks>
    /// DEUX CONDITIONS, ET LA SECONDE EST LA PLUS IMPORTANTE : le statut
    /// PENDING_PAYMENT, ET une intention de paiement rattachée. Sans la
    /// seconde, ce balayage emporterait les commandes des PARTENAIRES, qui
    /// naissent dans le même statut et n'ont jamais d'intention parce que leur
    /// règlement n'est pas tranché. Elles resteraient ouvertes pour toujours ;
    /// elles doivent rester ouvertes pour toujours, jusqu'à ce qu'on tranche.
    /// </remarks>
    Task<IReadOnlyList<DeliveryAggregate>> ListUnpaidBeforeAsync(
        DateTimeOffset limite,
        int batchSize,
        CancellationToken cancellationToken);

    /// <summary>
    /// Ce qui a ete FACTURE a un client : somme des prix des courses
    /// LIVREES, et leur nombre.
    ///
    /// FACTURE N'EST PAS ENCAISSE, et l'ecran doit le dire. Ce total ignore
    /// les impayes, les remboursements et les annulations posterieures au
    /// paiement : ces faits vivent dans Payment, qui agrege par PAYEUR et non
    /// par client — une course commandee par un commercant est payee par lui.
    /// Le chiffre rendu ici est celui de l'instantane de prix fige a la
    /// commande, sur les courses effectivement livrees. Rien d'autre.
    ///
    /// LA SOMME SE FAIT EN BASE, pas en memoire : additionner cote service
    /// obligerait a charger tout l'historique d'un client pour un seul
    /// nombre.
    /// </summary>
    /// <remarks>Le total est un <c>long</c> : le franc CFA n'a pas de subdivision.</remarks>
    Task<(int Count, long BilledTotal)> SumBilledForCustomerAsync(
        string customerId,
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

    /// <summary>
    /// Statuts retirés d'office, quoi que l'appelant demande.
    /// </summary>
    ///
    /// <remarks>
    /// CE N'EST PAS UN FILTRE, C'EST UNE REGLE DE VISIBILITE, et c'est pour cela
    /// qu'il est distinct de <see cref="Statuses"/> : celui-là rétrécit ce que
    /// l'appelant voyait déjà, celui-ci lui retire quelque chose qu'il ne doit
    /// pas voir. Un client qui demanderait explicitement PENDING_PAYMENT ne doit
    /// pas l'obtenir pour autant ; deux champs séparés rendent la chose
    /// impossible à contourner par le paramètre.
    /// </remarks>
    public IReadOnlyCollection<DeliveryStatus>? ExcludedStatuses { get; init; }

    public DateTimeOffset? CreatedAfter { get; init; }

    public DateTimeOffset? CreatedBefore { get; init; }

    public int PageSize { get; init; } = 25;

    public int Offset { get; init; }
}
