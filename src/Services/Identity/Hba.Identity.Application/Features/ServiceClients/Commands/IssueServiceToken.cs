using Hba.BuildingBlocks.Application.Abstractions;
using Hba.BuildingBlocks.Application.Messaging;
using Hba.BuildingBlocks.Domain;
using Hba.Identity.Application.Common.Interfaces;
using Hba.Identity.Application.Common.Views;

namespace Hba.Identity.Application.Features.ServiceClients.Commands;

public sealed record IssueServiceTokenCommand(string? ClientId, string? ClientSecret)
    : ICommand<ServiceTokenView>;

/// <summary>
/// Jeton d'un service, pour les appels qu'aucun utilisateur n'a déclenchés :
/// un consommateur Kafka, un planificateur.
/// </summary>
public sealed class IssueServiceTokenHandler(
    IServiceClientRegistry registry,
    ITokenIssuer tokens,
    IClock clock) : ICommandHandler<IssueServiceTokenCommand, ServiceTokenView>
{
    public Task<ServiceTokenView> HandleAsync(
        IssueServiceTokenCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        // Service inconnu et secret faux donnent la même réponse, comme pour
        // un partenaire : la réponse ne dit pas quels services existent.
        if (!registry.Verify(command.ClientId, command.ClientSecret))
        {
            throw new ForbiddenException("Identifiants de service invalides.");
        }

        var principal = new PrincipalView
        {
            // Le préfixe rend le sujet impossible à confondre avec un
            // identifiant de compte dans les journaux et les événements.
            SubjectId = $"service:{command.ClientId}",
            DisplayName = command.ClientId!,

            // UN SEUL ROLE, « service », ET C'EST UN AJOUT DU 30 SEPTEMBRE 2026.
            //
            // CE JETON N'EN PORTAIT AUCUN, et c'était présenté comme la
            // garantie principale. C'était surtout une impasse : les services
            // appelés sont en [Authorize], donc tout appel de fond revenait
            // « non authentifié ». Deux chemins en mouraient en silence — le
            // reçu de course (Notification qui demande l'adresse à Directory) et
            // l'effacement de la photo à la suppression d'un compte (Directory
            // qui appelle Media) — et l'échec était avalé en avertissement.
            //
            // LE ROLE NE DONNE PRESQUE RIEN, ET C'EST LE POINT. Il n'est pas
            // dans BackOffice, il n'ouvre aucune écriture, et chaque service
            // appelé décide explicitement des lectures qu'il accorde. Toute
            // règle métier qui demande QUI agit continue de le refuser : un
            // service n'est personne, il ne prend pas de course et ne lit pas de
            // dossier.
            Roles = [Domain.Roles.Service],
        };

        var token = tokens.IssueAccessToken(principal, Guid.CreateVersion7(), clock.UtcNow);

        return Task.FromResult(new ServiceTokenView(
            token,
            (int)tokens.AccessTokenLifetime.TotalSeconds));
    }
}
