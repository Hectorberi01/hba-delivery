using Hba.BuildingBlocks.Application.Abstractions;
using Hba.BuildingBlocks.Grpc.ServiceAuth;
using Hba.BuildingBlocks.Grpc.Extensions;
using Hba.BuildingBlocks.Messaging.Extensions;
using Hba.BuildingBlocks.Messaging.Inbox;
using Hba.BuildingBlocks.Messaging.Outbox;
using Hba.BuildingBlocks.Persistence;
using Hba.Contracts.Media.V1;
using Hba.Directory.Application.Ports;
using Hba.Directory.Infrastructure.MediaAccess;
using Hba.Directory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Hba.Directory.Infrastructure.Extensions;

public static class DirectoryInfrastructureExtensions
{
    public static IServiceCollection AddDirectoryInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // POURQUOI CE DIAGNOSTIC EXISTE, ET POURQUOI IL EST DERRIERE UN
        // INTERRUPTEUR.
        //
        // « expected to affect 1 row(s), but actually affected 0 » ne dit pas
        // QUELLE ligne EF croyait modifier, ni pourquoi il a choisi un UPDATE
        // plutot qu'un INSERT. Les valeurs des parametres sont masquees par
        // defaut — a juste titre : elles contiennent des telephones et des
        // adresses, et un journal de production n'est pas un annuaire.
        //
        // DirectoryDb__Trace=true les demasque, ET ajoute l'etat du
        // ChangeTracker au moment de l'enregistrement. Les deux ensemble
        // repondent a la seule question qui reste : l'entite est-elle
        // « Added » ou « Modified », et avec quelle cle.
        //
        // IL REFUSE DE S'ACTIVER HORS DEVELOPPEMENT. Un mot de passe de base,
        // un telephone et une adresse finiraient sinon en clair dans les
        // journaux d'un serveur.
        var trace = configuration.GetValue("DirectoryDb:Trace", false);

        services.AddDbContext<DirectoryDbContext>((sp, options) =>
        {
            options.UseNpgsql(
                configuration.Obligatoire("DirectoryDb"),
                npgsql => npgsql.MigrationsHistoryTable("__ef_migrations", DirectoryDbContext.Schema));

            if (!trace)
            {
                return;
            }

            var environnement = sp.GetService<IHostEnvironment>();

            if (environnement is not null && !environnement.IsDevelopment())
            {
                sp.GetRequiredService<ILogger<DirectoryDbContext>>().LogError(
                    "DirectoryDb:Trace est demande hors developpement : IGNORE. "
                    + "Il afficherait telephones et adresses en clair dans les journaux.");

                return;
            }

            options.EnableSensitiveDataLogging();
            options.EnableDetailedErrors();
        });

        services.AddHbaAutoMigration<DirectoryDbContext>();
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<DirectoryDbContext>());
        services.AddScoped<ICustomerRepository, CustomerRepository>();
        services.AddScoped<IMerchantRepository, MerchantRepository>();

        services.AddSingleton(DirectoryDbContext.MessagingTables);
        services.AddScoped<IOutbox>(sp => new EfOutbox(sp.GetRequiredService<DirectoryDbContext>()));
        services.AddScoped<IOutboxStore>(sp => new EfOutboxStore(
            sp.GetRequiredService<DirectoryDbContext>(),
            DirectoryDbContext.MessagingTables));
        services.AddScoped<IInboxStore>(sp => new EfInboxStore(sp.GetRequiredService<DirectoryDbContext>()));
        services.AddScoped<IIdempotencyStore>(sp => new EfIdempotencyStore(sp.GetRequiredService<DirectoryDbContext>()));

        // JOURNAL DES LECTURES DE DONNEES PERSONNELLES. Il ecrit dans la base
        // de CE service : la regle « une base par service » vaut aussi pour
        // un journal d'audit, et la fiche client comme le cumul facture se
        // lisent chacun chez soi.
        services.AddScoped<IPersonalDataReadLog>(sp => new EfPersonalDataReadLog(
            sp.GetRequiredService<DirectoryDbContext>(),
            sp.GetRequiredService<ICallerContext>(),
            sp.GetRequiredService<ILogger<EfPersonalDataReadLog>>()));

        // SA PURGE, ETEINTE TANT QUE RIEN N'EST REGLE. La duree de
        // conservation d'une trace d'acces est une decision juridique ; le
        // code pose le mecanisme et l'annonce au demarrage, il ne choisit pas
        // le nombre de mois.
        services.AddHbaPersonalDataReadPurge<DirectoryDbContext>(configuration);

        services.AddScoped<IPersonalDataReadReader>(sp =>
            new EfPersonalDataReadReader(sp.GetRequiredService<DirectoryDbContext>()));

        // LE LIEN VERS MEDIA (point 27). Directory ne stocke pas de fichier :
        // il garde un identifiant de media, et demande a Media de le decrire,
        // de le signer ou de l'effacer.
        //
        // C'EST BIEN DIRECTORY QUI APPELLE, ET PAS LA PASSERELLE. La regle du
        // referentiel — « toute autorisation se verifie cote service, jamais
        // seulement cote BFF » — veut que celui qui detient la donnee soit
        // celui qui decide qui la voit. Une passerelle qui aurait signe
        // elle-meme aurait ete crue sur parole.
        services.AddHbaGrpcClient<MediaService.MediaServiceClient>(
            new Uri(configuration["Services:Media"] ?? "http://media:8081"));

        // LE JETON DE SERVICE, POUR LE SEUL APPEL QUI N'A PERSONNE DERRIERE.
        //
        // Les lectures de media partent d'une requete d'utilisateur et reportent
        // son jeton — c'est ce qui fait que le journal de Media dit qui a voulu
        // voir une photo. L'effacement, lui, descend du consommateur
        // « AccountErased » : l'appel partait nu, Media le refusait, et la photo
        // survivait a la suppression du compte.
        services.AddHbaServiceToken(configuration);

        services.AddScoped<IMediaCatalogue, MediaCatalogue>();

        services.AddHbaMessaging(configuration, producerName: "directory", consumerGroupId: "directory");

        return services;
    }
}
