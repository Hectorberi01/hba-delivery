namespace Hba.BuildingBlocks.Domain;

/// <summary>
/// Entité identifiée par un <see cref="Guid"/>. L'égalité porte sur l'identité,
/// jamais sur l'état.
/// </summary>
public abstract class Entity
{
    protected Entity(Guid id)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("L'identifiant d'une entité ne peut pas être vide.", nameof(id));
        }

        Id = id;
    }

    /// <summary>Constructeur destiné aux matérialiseurs (EF Core).</summary>
    protected Entity()
    {
    }

    public Guid Id { get; protected set; }

    public override bool Equals(object? obj)
        => obj is Entity other && other.GetType() == GetType() && other.Id == Id;

    public override int GetHashCode() => HashCode.Combine(GetType(), Id);
}
