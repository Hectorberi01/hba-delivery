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

            // AUCUN ROLE, ET C'EST LA GARANTIE PRINCIPALE. Le référentiel
            // acteurs ne connaît pas de rôle « service » : un service n'est
            // pas un acteur. Sans rôle, ce jeton passe l'authentification et
            // échoue sur toute règle métier qui demande qui est l'appelant —
            // il ne peut donc pas prendre une course ni lire un dossier.
            Roles = [],
        };

        var token = tokens.IssueAccessToken(principal, Guid.CreateVersion7(), clock.UtcNow);

        return Task.FromResult(new ServiceTokenView(
            token,
            (int)tokens.AccessTokenLifetime.TotalSeconds));
    }
}
