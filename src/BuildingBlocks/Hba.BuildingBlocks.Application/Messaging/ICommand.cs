namespace Hba.BuildingBlocks.Application.Messaging;

/// <summary>Intention de changer l'état du système.</summary>
public interface ICommand<TResult>;

/// <summary>Commande sans valeur de retour.</summary>
public interface ICommand : ICommand<Unit>;

public interface ICommandHandler<in TCommand, TResult>
    where TCommand : ICommand<TResult>
{
    Task<TResult> HandleAsync(TCommand command, CancellationToken cancellationToken);
}

/// <summary>Lecture, sans effet de bord.</summary>
public interface IQuery<TResult>;

public interface IQueryHandler<in TQuery, TResult>
    where TQuery : IQuery<TResult>
{
    Task<TResult> HandleAsync(TQuery query, CancellationToken cancellationToken);
}

/// <summary>Absence de valeur de retour.</summary>
public readonly record struct Unit
{
    public static readonly Unit Value = default;
}
