using System.Reflection;
using FluentValidation;
using Hba.BuildingBlocks.Application.Abstractions;
using Hba.BuildingBlocks.Application.Messaging;
using Microsoft.Extensions.DependencyInjection;

namespace Hba.BuildingBlocks.Application.Extensions;

public static class ApplicationServiceCollectionExtensions
{
    /// <summary>
    /// Enregistre le dispatcher, l'horloge et tous les handlers et validateurs de
    /// l'assembly passé en paramètre.
    /// </summary>
    public static IServiceCollection AddHbaApplication(this IServiceCollection services, Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(assembly);

        services.AddSingleton<IClock, SystemClock>();
        services.AddScoped<IDispatcher, Dispatcher>();
        services.AddValidatorsFromAssembly(assembly, includeInternalTypes: true);

        foreach (var type in assembly.GetTypes().Where(t => t is { IsAbstract: false, IsInterface: false }))
        {
            foreach (var @interface in type.GetInterfaces().Where(IsHandlerInterface))
            {
                services.AddScoped(@interface, type);
            }
        }

        return services;
    }

    private static bool IsHandlerInterface(Type type)
    {
        if (!type.IsGenericType)
        {
            return false;
        }

        var definition = type.GetGenericTypeDefinition();
        return definition == typeof(ICommandHandler<,>) || definition == typeof(IQueryHandler<,>);
    }
}
