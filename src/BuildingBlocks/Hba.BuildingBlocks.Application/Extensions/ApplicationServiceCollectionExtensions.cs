using System.Reflection;
using FluentValidation;
using Hba.BuildingBlocks.Application.Abstractions;
using Hba.BuildingBlocks.Application.Messaging;
using Hba.BuildingBlocks.Application.Time;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

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

    /// <summary>
    /// Enregistre le calendrier métier : fuseau, fenêtre par défaut, plafond.
    ///
    /// A APPELER DANS TOUT SERVICE QUI AGREGE. Sans lui, chacun redéciderait
    /// ce qu'est « aujourd'hui », et deux écrans montreraient deux chiffres
    /// pour la même journée. La validation du fuseau se fait au démarrage, pas
    /// au premier appel : une console qui tombe à midi parce qu'un fuseau est
    /// mal écrit coûte plus cher qu'un service qui refuse de démarrer.
    /// </summary>
    public static IServiceCollection AddHbaTime(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<TimeOptions>()
            .Bind(configuration.GetSection(TimeOptions.SectionName))
            .Validate(
                o => o.DefaultWindowDays is > 0 and <= 366,
                "Time:DefaultWindowDays doit tenir entre 1 et 366 jours.")
            .Validate(
                o => o.MaxWindowDays >= o.DefaultWindowDays,
                "Time:MaxWindowDays ne peut pas être inférieur à Time:DefaultWindowDays.")
            .ValidateOnStart();

        // TryAdd : AddHbaApplication pose déjà l'horloge, et l'ordre des deux
        // appels ne doit pas décider laquelle est résolue.
        services.TryAddSingleton<IClock, SystemClock>();
        services.TryAddSingleton<ITimeCalendar, TimeCalendar>();

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
