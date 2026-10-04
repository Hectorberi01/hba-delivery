using Hba.BuildingBlocks.Application.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace Hba.Rating.Application;

public static class RatingApplicationExtensions
{
    public static IServiceCollection AddRatingApplication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddHbaApplication(typeof(RatingApplicationExtensions).Assembly);

        return services;
    }
}
