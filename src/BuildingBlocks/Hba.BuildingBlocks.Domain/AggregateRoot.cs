namespace Hba.BuildingBlocks.Domain;

/// <summary>
/// Racine d'agrégat. Seule porte d'entrée des modifications : toute transition
/// passe par une méthode de l'agrégat, jamais par une affectation depuis
/// l'extérieur.
/// </summary>
public abstract class AggregateRoot : Entity
{
    private readonly List<IDomainEvent> _domainEvents = [];

    protected AggregateRoot(Guid id) : base(id)
    {
    }

    protected AggregateRoot()
    {
    }

    /// <summary>
    /// Jeton de concurrence optimiste. Mappé sur une colonne xmin PostgreSQL par
    /// l'infrastructure : deux acceptations concurrentes ne peuvent pas gagner.
    /// </summary>
    public uint Version { get; protected set; }

    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    protected void Raise(IDomainEvent domainEvent)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        _domainEvents.Add(domainEvent);
    }

    public void ClearDomainEvents() => _domainEvents.Clear();
}
