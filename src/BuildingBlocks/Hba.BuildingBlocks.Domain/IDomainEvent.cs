namespace Hba.BuildingBlocks.Domain;

/// <summary>
/// Fait métier déjà survenu à l'intérieur d'un agrégat. Reste dans le service :
/// sa traduction éventuelle en message Kafka est faite par la couche Application.
/// </summary>
public interface IDomainEvent
{
    Guid EventId { get; }

    DateTimeOffset OccurredAt { get; }

    /// <summary>Acteur à l'origine du fait. Obligatoire : tout est audité.</summary>
    Actor Actor { get; }
}

/// <summary>Base commune des événements de domaine.</summary>
public abstract record DomainEvent(Actor Actor, DateTimeOffset OccurredAt) : IDomainEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
}
