using Hba.BuildingBlocks.Application.Abstractions;
using Hba.BuildingBlocks.Domain;
using Hba.Identity.Application.Ports;
using Hba.Identity.Domain;
using Hba.Identity.Domain.Accounts;
using Hba.Identity.Domain.ValueObjects;
using Microsoft.Extensions.Options;

namespace Hba.Identity.Api.Bootstrap;

public sealed class BootstrapOptions
{
    public const string SectionName = "Bootstrap";

    public string? AdminEmail { get; set; }

    public string? AdminPassword { get; set; }

    public string AdminDisplayName { get; set; } = "Administrateur";
}

/// <summary>
/// Crée le premier administrateur.
///
/// Sans cela, le système est fermé sur lui-même : créer un compte de
/// back-office exige déjà le rôle admin, donc personne ne peut créer le
/// premier. L'alternative était une insertion SQL à la main, avec une empreinte
/// PBKDF2 calculée à part — faisable une fois, pénible à documenter et facile à
/// rater.
///
/// Le service est idempotent : dès qu'un administrateur existe, il ne fait
/// plus rien, même si la configuration est restée en place. Le mot de passe
/// d'amorçage doit être changé à la première connexion ; rien ne l'impose
/// encore, c'est signalé dans les points à trancher.
/// </summary>
public sealed class AdminBootstrap(
    IServiceScopeFactory scopeFactory,
    IOptions<BootstrapOptions> options,
    IHostEnvironment environment,
    ILogger<AdminBootstrap> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;

        if (string.IsNullOrWhiteSpace(settings.AdminEmail) || string.IsNullOrWhiteSpace(settings.AdminPassword))
        {
            return;
        }

        using var scope = scopeFactory.CreateScope();
        var accounts = scope.ServiceProvider.GetRequiredService<IAccountRepository>();

        if (await accounts.AnyWithRoleAsync(Roles.Admin, cancellationToken).ConfigureAwait(false))
        {
            logger.LogInformation("Un administrateur existe déjà : amorçage ignoré.");
            return;
        }

        var email = EmailAddress.Create(settings.AdminEmail);

        if (await accounts.GetByEmailAsync(email.Value, cancellationToken).ConfigureAwait(false) is not null)
        {
            logger.LogWarning(
                "Un compte porte déjà {Email} sans être administrateur : amorçage ignoré.",
                email.Value);
            return;
        }

        var clock = scope.ServiceProvider.GetRequiredService<IClock>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var account = Account.CreateBackOffice(
            Guid.CreateVersion7(),
            email,
            settings.AdminDisplayName,
            [Roles.Admin],
            PasswordHash.FromPlainText(settings.AdminPassword),
            // L'auteur de cette création n'est pas une personne : c'est
            // l'amorçage lui-même, et l'audit doit le dire.
            Actor.Human(ActorKind.Admin, "bootstrap", "Amorçage"),
            clock.UtcNow);

        accounts.Add(account);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        logger.LogWarning(
            "Premier administrateur créé : {Email}. Changez son mot de passe dès la première connexion, "
            + "puis retirez Bootstrap:AdminPassword de la configuration.",
            email.Value);

        if (!environment.IsDevelopment())
        {
            logger.LogWarning(
                "Le mot de passe d'amorçage est encore présent dans la configuration de {Environment}.",
                environment.EnvironmentName);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
