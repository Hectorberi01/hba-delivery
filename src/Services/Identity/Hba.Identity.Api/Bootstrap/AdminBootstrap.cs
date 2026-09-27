using Hba.BuildingBlocks.Application.Abstractions;
using Hba.BuildingBlocks.Domain;
using Hba.Identity.Application.Common.Interfaces;
using Hba.Identity.Domain;
using Hba.Identity.Domain.Accounts;
using Hba.Identity.Domain.Interfaces;
using Hba.Identity.Domain.ValueObjects;
using Microsoft.Extensions.Options;

namespace Hba.Identity.Api.Bootstrap;

public sealed class BootstrapOptions
{
    public const string SectionName = "Bootstrap";

    public string? AdminEmail { get; set; }

    public string? AdminPassword { get; set; }

    public string AdminDisplayName { get; set; } = "Administrateur";

    /// <summary>
    /// Repose le mot de passe du compte d'amorçage s'il existe déjà.
    ///
    /// EXISTE PARCE QUE L'AMORÇAGE EST IDEMPOTENT, ET QUE C'EST UNE IMPASSE
    /// QUAND ON A PERDU LE MOT DE PASSE. Dès qu'un administrateur existe, la
    /// création est ignorée ; et changer un mot de passe exige d'être déjà
    /// connecté. Sans cette porte, un poste de développement dont personne ne
    /// connaît plus le compte admin ne peut plus rien administrer du tout.
    ///
    /// REFUSÉE HORS DEVELOPMENT, comme Otp:FixedCodeForDevelopment : en
    /// production, elle rendrait un mot de passe posé dans la configuration
    /// suffisant pour reprendre le compte administrateur à chaque démarrage.
    /// </summary>
    public bool ResetPassword { get; set; }
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

        if (settings.ResetPassword && !environment.IsDevelopment())
        {
            throw new InvalidOperationException(
                $"Bootstrap:ResetPassword est activé en environnement {environment.EnvironmentName} : "
                + "le mot de passe de la configuration reprendrait le compte administrateur à chaque "
                + "démarrage. Retirez ce réglage.");
        }

        using var scope = scopeFactory.CreateScope();
        var accounts = scope.ServiceProvider.GetRequiredService<IAccountRepository>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var email = EmailAddress.Create(settings.AdminEmail);
        var existant = await accounts.GetByEmailAsync(email.Value, cancellationToken).ConfigureAwait(false);

        if (settings.ResetPassword && existant is not null)
        {
            // ON NE PROMEUT PERSONNE ICI. Reposer un mot de passe est une
            // reprise en main ; ajouter le rôle admin à un compte qui ne l'a
            // pas serait une escalade, et elle passerait inaperçue parce que
            // personne ne lit les journaux de démarrage.
            if (!existant.Roles.Contains(Roles.Admin))
            {
                logger.LogError(
                    "Le compte {Email} n'est pas administrateur : son mot de passe n'a pas été reposé. "
                    + "Choisissez une autre adresse pour Bootstrap:AdminEmail.",
                    email.Value);
                return;
            }

            existant.SetPassword(
                PasswordHash.FromPlainText(settings.AdminPassword),
                Actor.Human(ActorKind.Admin, "bootstrap", "Amorçage"),
                clock.UtcNow);

            await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            logger.LogWarning(
                "Mot de passe de {Email} reposé par l'amorçage. Retirez Bootstrap:ResetPassword "
                + "dès que vous êtes entré.",
                email.Value);

            return;
        }

        if (existant is not null)
        {
            logger.LogWarning(
                "Un compte porte déjà {Email} : amorçage ignoré. Bootstrap:ResetPassword=true reposerait "
                + "son mot de passe, en Development uniquement.",
                email.Value);
            return;
        }

        // SANS RESETPASSWORD, UN SEUL ADMINISTRATEUR SUFFIT À TOUT ARRÊTER.
        // C'est la garde d'origine : l'amorçage ne sert qu'à sortir de
        // l'impasse du tout premier compte, pas à peupler l'équipe.
        if (!settings.ResetPassword
            && await accounts.AnyWithRoleAsync(Roles.Admin, cancellationToken).ConfigureAwait(false))
        {
            logger.LogInformation(
                "Un administrateur existe déjà : amorçage ignoré. Si son mot de passe est perdu, "
                + "Bootstrap:ResetPassword=true en Development crée ou reprend {Email}.",
                email.Value);
            return;
        }

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
