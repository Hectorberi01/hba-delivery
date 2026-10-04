using Hba.BuildingBlocks.Application.Abstractions;
using Hba.BuildingBlocks.Messaging.Extensions;
using Hba.BuildingBlocks.Messaging.Inbox;
using Hba.BuildingBlocks.Messaging.Outbox;
using Hba.BuildingBlocks.Persistence;
using Hba.Rating.Application.Common;
using Hba.Rating.Infrastructure.Persistence;
using Hba.Rating.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Hba.Rating.Infrastructure;

public static class RatingInfrastructureExtensions
{
    public static IServiceCollection AddRatingInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddDbContext<RatingDbContext>(options =>
            options.UseNpgsql(
                configuration.Obligatoire("RatingDb"),
                npgsql => npgsql.MigrationsHistoryTable("__ef_migrations", RatingDbContext.Schema)));

        services.AddHbaAutoMigration<RatingDbContext>();
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<RatingDbContext>());
        services.AddScoped<IReviewRepository, ReviewRepository>();

        services.AddSingleton(RatingDbContext.MessagingTables);
        services.AddScoped<IOutbox>(sp => new EfOutbox(sp.GetRequiredService<RatingDbContext>()));
        services.AddScoped<IOutboxStore>(sp => new EfOutboxStore(
            sp.GetRequiredService<RatingDbContext>(),
            RatingDbContext.MessagingTables));
        services.AddScoped<IInboxStore>(sp => new EfInboxStore(sp.GetRequiredService<RatingDbContext>()));
        services.AddScoped<IIdempotencyStore>(sp => new EfIdempotencyStore(sp.GetRequiredService<RatingDbContext>()));

        services.AddHbaMessaging(configuration, producerName: "rating", consumerGroupId: "rating");

        return services;
    }
}
