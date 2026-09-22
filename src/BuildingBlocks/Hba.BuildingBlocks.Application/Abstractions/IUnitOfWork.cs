namespace Hba.BuildingBlocks.Application.Abstractions;

/// <summary>
/// Une seule transaction couvre l'écriture métier ET l'insertion des messages
/// d'Outbox. C'est ce qui rend la publication fiable sans transaction distribuée.
/// </summary>
public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}
