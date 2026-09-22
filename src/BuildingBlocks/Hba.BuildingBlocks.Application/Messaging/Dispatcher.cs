using FluentValidation;
using Hba.BuildingBlocks.Domain;
using Microsoft.Extensions.DependencyInjection;

namespace Hba.BuildingBlocks.Application.Messaging;

/// <summary>
/// Point d'entrée unique de la couche Application. Résout le handler, applique
/// le pipeline de validation, exécute. Pas de bibliothèque de médiation tierce :
/// le besoin ne le justifie pas et cela garde la pile d'appel lisible.
/// </summary>
public interface IDispatcher
{
    Task<TResult> SendAsync<TResult>(ICommand<TResult> command, CancellationToken cancellationToken);

    Task<TResult> QueryAsync<TResult>(IQuery<TResult> query, CancellationToken cancellationToken);
}

internal sealed class Dispatcher(IServiceProvider services) : IDispatcher
{
    public async Task<TResult> SendAsync<TResult>(ICommand<TResult> command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        await ValidateAsync(command, cancellationToken).ConfigureAwait(false);

        var handlerType = typeof(ICommandHandler<,>).MakeGenericType(command.GetType(), typeof(TResult));
        var handler = services.GetService(handlerType)
            ?? throw new InvalidOperationException($"Aucun handler enregistré pour {command.GetType().Name}.");

        return await InvokeAsync<TResult>(handler, command, cancellationToken).ConfigureAwait(false);
    }

    public async Task<TResult> QueryAsync<TResult>(IQuery<TResult> query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        await ValidateAsync(query, cancellationToken).ConfigureAwait(false);

        var handlerType = typeof(IQueryHandler<,>).MakeGenericType(query.GetType(), typeof(TResult));
        var handler = services.GetService(handlerType)
            ?? throw new InvalidOperationException($"Aucun handler enregistré pour {query.GetType().Name}.");

        return await InvokeAsync<TResult>(handler, query, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<TResult> InvokeAsync<TResult>(object handler, object message, CancellationToken cancellationToken)
    {
        var method = handler.GetType().GetMethod("HandleAsync")
            ?? throw new InvalidOperationException($"HandleAsync introuvable sur {handler.GetType().Name}.");

        var task = (Task<TResult>?)method.Invoke(handler, [message, cancellationToken])
            ?? throw new InvalidOperationException($"{handler.GetType().Name}.HandleAsync a renvoyé null.");

        return await task.ConfigureAwait(false);
    }

    private async Task ValidateAsync(object message, CancellationToken cancellationToken)
    {
        var validatorType = typeof(IValidator<>).MakeGenericType(message.GetType());
        var validators = services.GetServices(validatorType).OfType<IValidator>().ToList();
        if (validators.Count == 0)
        {
            return;
        }

        var context = new ValidationContext<object>(message);
        var failures = new List<FluentValidation.Results.ValidationFailure>();

        foreach (var validator in validators)
        {
            var result = await validator.ValidateAsync(context, cancellationToken).ConfigureAwait(false);
            failures.AddRange(result.Errors);
        }

        if (failures.Count > 0)
        {
            var details = string.Join(" ; ", failures.Select(f => $"{f.PropertyName}: {f.ErrorMessage}"));
            throw new DomainException("VALIDATION_FAILED", details);
        }
    }
}
